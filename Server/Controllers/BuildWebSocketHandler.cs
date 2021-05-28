namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Utilities;
    using Hangfire;
    using Hubs;
    using Jobs;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Primitives;
    using Models;
    using Shared.Models;
    using Shared.Notifications;
    using Utilities;

    public class BuildWebSocketHandler
    {
        /// <summary>
        ///   Not every log message is written to the DB to save on performance
        /// </summary>
        private static readonly TimeSpan OutputSaveInterval = TimeSpan.FromSeconds(15);

        private readonly ILogger<BuildWebSocketHandler> logger;
        private readonly RealTimeBuildMessageSocket socket;
        private readonly NotificationsEnabledDb database;
        private readonly IHubContext<NotificationsHub, INotifications> notifications;
        private readonly CiJob job;

        private readonly string notificationGroup;
        private readonly StringBuilder outputSectionText = new();

        private long sectionNumberCounter;
        private CiJobOutputSection activeSection;

        private DateTime lastSaved = DateTime.UtcNow;

        private BuildWebSocketHandler(ILogger<BuildWebSocketHandler> logger, WebSocket socket,
            NotificationsEnabledDb database, IHubContext<NotificationsHub, INotifications> notifications, CiJob job)
        {
            this.logger = logger;
            this.socket = new RealTimeBuildMessageSocket(socket);
            this.database = database;
            this.notifications = notifications;
            this.job = job;

            notificationGroup = NotificationGroups.CIProjectsBuildsJobRealtimeOutputPrefix + job.CiProjectId + "_" +
                job.CiBuildId + "_" + job.CiJobId;
        }

        public static async Task HandleHttpConnection(HttpContext context, IServiceProvider serviceProvider)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<BuildWebSocketHandler>>();
                if (!context.Request.Query.TryGetValue("key", out StringValues keyRaw) || keyRaw.Count != 1 ||
                    !Guid.TryParse(keyRaw[0], out Guid key))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                var database = serviceProvider.GetRequiredService<NotificationsEnabledDb>();

                var job = await database.CiJobs.AsQueryable()
                    .WhereHashed(nameof(CiJob.BuildOutputConnectKey), key.ToString())
                    .AsAsyncEnumerable().FirstOrDefaultAsync(b => b.BuildOutputConnectKey == key);

                if (job == null || job.State == CIJobState.Finished)
                {
                    logger.LogWarning("Invalid job was tried to have output connected to it");
                    context.Response.ContentType = "plain/text";
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    await context.Response.Body.WriteAsync(
                        Encoding.UTF8.GetBytes("Invalid access key or job isn't accepting output anymore"));
                    return;
                }

                using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();

                logger.LogInformation(
                    "Accepted build output connection for job {CIProjectId}-{CIBuildId}-{CIJobId} " +
                    "from {RemoteIpAddress}", job.CiProjectId, job.CiBuildId, job.CiJobId,
                    context.Connection.RemoteIpAddress);

                var handler = new BuildWebSocketHandler(logger, webSocket, database,
                    serviceProvider.GetRequiredService<IHubContext<NotificationsHub, INotifications>>(), job);

                await handler.Run();

                logger.LogInformation("Build output from {RemoteIpAddress} closed",
                    context.Connection.RemoteIpAddress);
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            }
        }

        private async Task Run()
        {
            // Detect existing sections (if this is a reconnection)
            sectionNumberCounter = await database.CiJobOutputSections.AsQueryable()
                .Where(s => s.CiProjectId == job.CiProjectId && s.CiBuildId == job.CiBuildId &&
                    s.CiJobId == job.CiJobId).MaxAsync(s => (long?)s.CiJobOutputSectionId) ?? 0;

            if (sectionNumberCounter > 0)
            {
                logger.LogInformation(
                    "Re-starting section numbering at (this is maybe a reconnect): {SectionNumberCounter}",
                    sectionNumberCounter);

                // Fetch the open section if there is one
                activeSection = await database.CiJobOutputSections.AsQueryable()
                    .FirstOrDefaultAsync(s => s.CiProjectId == job.CiProjectId && s.CiBuildId == job.CiBuildId &&
                        s.CiJobId == job.CiJobId && s.Status == CIJobSectionStatus.Running);

                if (activeSection != null)
                {
                    logger.LogInformation("Continuing filling from section: {Name}", activeSection.Name);
                }
            }

            bool error = false;

            while (!socket.CloseStatus.HasValue)
            {
                RealTimeBuildMessage message;
                try
                {
                    var readResult = await socket.Read(CancellationToken.None);

                    if (readResult.closed)
                        break;

                    message = readResult.message;
                }
                catch (WebSocketBuildMessageTooLongException e)
                {
                    logger.LogError("Received too long realTimeBuildMessage: {@E}", e);
                    await SendMessage(new RealTimeBuildMessage()
                    {
                        Type = BuildSectionMessageType.Error,
                        ErrorMessage =
                            "Too long realTimeBuildMessage, can't receive, stopping realTimeBuildMessage processing"
                    });

                    error = true;
                    break;
                }
                catch (WebSocketBuildMessageLengthMisMatchException e)
                {
                    logger.LogError("Read realTimeBuildMessage length doesn't match reported length: {@E}", e);
                    await SendMessage(new RealTimeBuildMessage()
                    {
                        Type = BuildSectionMessageType.Error,
                        ErrorMessage =
                            "RealTimeBuildMessage read and reported size mismatch, stopping " +
                            "realTimeBuildMessage processing"
                    });

                    error = true;
                    break;
                }
                catch (InvalidWebSocketBuildMessageFormatException e)
                {
                    logger.LogError("Failed to parse a received realTimeBuildMessage: {@E}", e);
                    await SendMessage(new RealTimeBuildMessage()
                    {
                        Type = BuildSectionMessageType.Error,
                        ErrorMessage = "Can't process realTimeBuildMessage, invalid format or content"
                    });

                    continue;
                }
                catch (WebSocketProtocolException e)
                {
                    logger.LogWarning("Error reading build message from websocket: {@E}", e);
                    break;
                }

                if (message == null)
                    continue;

                var now = DateTime.UtcNow;

                switch (message.Type)
                {
                    case BuildSectionMessageType.SectionStart:
                    {
                        // Start a new section
                        if (activeSection != null)
                        {
                            logger.LogError(
                                "Received a build output section start ({Name}) while there's an active section ({SectionName})",
                                activeSection.Name, message.SectionName);
                            await SendMessage(new RealTimeBuildMessage()
                            {
                                Type = BuildSectionMessageType.Error,
                                ErrorMessage = "Can't start a new section while one is in progress"
                            });
                        }
                        else if (string.IsNullOrEmpty(message.SectionName) || message.SectionName.Length > 100)
                        {
                            logger.LogError("Received a build output section start with missing or too long name");
                            await SendMessage(new RealTimeBuildMessage()
                            {
                                Type = BuildSectionMessageType.Error,
                                ErrorMessage = "Can't start a new section with invalid name"
                            });
                        }
                        else
                        {
                            activeSection = new CiJobOutputSection()
                            {
                                CiProjectId = job.CiProjectId,
                                CiBuildId = job.CiBuildId,
                                CiJobId = job.CiJobId,
                                CiJobOutputSectionId = ++sectionNumberCounter,
                                Name = message.SectionName,
                                Output = message.Output ?? string.Empty
                            };

                            outputSectionText.Clear();
                            activeSection.CalculateOutputLength();
                            lastSaved = now;

                            await database.CiJobOutputSections.AddAsync(activeSection);
                            await database.SaveChangesAsync();

                            message.SectionId = activeSection.CiJobOutputSectionId;
                            await SendMessageToWebsiteClients(message);
                        }

                        break;
                    }
                    case BuildSectionMessageType.BuildOutput:
                    {
                        // Error if no active section
                        if (activeSection == null)
                        {
                            logger.LogError("Received a build output message but there is no active section");
                            await SendMessage(new RealTimeBuildMessage()
                            {
                                Type = BuildSectionMessageType.Error,
                                ErrorMessage = "No active output section"
                            });
                        }
                        else
                        {
                            // TODO: add total output amount limit here (if exceeded, make the build fail)

                            // Append to current section
                            outputSectionText.Append(message.Output);

                            // Save if time to do so
                            if (now - lastSaved > OutputSaveInterval)
                            {
                                lastSaved = now;
                                AddPendingOutputToActiveSection();

                                await database.SaveChangesAsync();
                            }

                            message.SectionId = activeSection.CiJobOutputSectionId;
                            await SendMessageToWebsiteClients(message);
                        }

                        break;
                    }
                    case BuildSectionMessageType.SectionEnd:
                    {
                        // Set the status of the last section and unset the active section
                        if (activeSection == null)
                        {
                            logger.LogError("Received a build section end but there is no active section");
                            await SendMessage(new RealTimeBuildMessage()
                            {
                                Type = BuildSectionMessageType.Error,
                                ErrorMessage = "No active output section"
                            });
                        }
                        else
                        {
                            activeSection.Status = message.WasSuccessful ?
                                CIJobSectionStatus.Succeeded :
                                CIJobSectionStatus.Failed;

                            // Append last pending text
                            AddPendingOutputToActiveSection();
                            lastSaved = now;

                            await database.SaveChangesAsync();

                            activeSection = null;
                        }

                        break;
                    }
                    case BuildSectionMessageType.FinalStatus:
                    {
                        if (activeSection != null)
                        {
                            logger.LogWarning("Last log section was not closed before final status");

                            // Assume section has the same result as the overall result
                            activeSection.Status = message.WasSuccessful ?
                                CIJobSectionStatus.Succeeded :
                                CIJobSectionStatus.Failed;

                            AddPendingOutputToActiveSection();
                            lastSaved = now;

                            await database.SaveChangesAsync();

                            activeSection = null;
                        }

                        // Queue a job to set build final status
                        BackgroundJob.Enqueue<SetFinishedCIJobStatusJob>(x => x.Execute(job.CiProjectId, job.CiBuildId,
                            job.CiJobId, message.WasSuccessful, CancellationToken.None));
                        return;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if (error)
            {
                // Add error message to build output
                if (activeSection != null)
                {
                    outputSectionText.Append("Critical error in processing websocket connection, " +
                        "no further output will be received\n");
                }
                else
                {
                    logger.LogWarning("Can't add error about build failing to read websocket data to job output");
                }
            }

            // Write remaining output
            if (outputSectionText.Length > 0)
            {
                if (activeSection != null)
                {
                    AddPendingOutputToActiveSection();
                }
                else
                {
                    logger.LogError("Can't add pending output, no active section: {ToString}",
                        outputSectionText.ToString());
                }
            }

            // And any pending sections
            if (activeSection != null)
            {
                outputSectionText.Append("Last section was not closed, marking it as failed\n");
                AddPendingOutputToActiveSection();

                activeSection.Status = CIJobSectionStatus.Failed;
                await database.SaveChangesAsync();
                activeSection = null;
            }

            if (error)
            {
                BackgroundJob.Schedule<SetFinishedCIJobStatusJob>(x => x.Execute(job.CiProjectId, job.CiBuildId,
                    job.CiJobId, false, CancellationToken.None), TimeSpan.FromSeconds(10));
            }
        }

        /// <summary>
        ///   Send realtime update to clients on the website
        /// </summary>
        /// <param name="message">The message to send</param>
        private async Task SendMessageToWebsiteClients(RealTimeBuildMessage message)
        {
            await notifications.Clients.Group(notificationGroup).ReceiveNotification(
                new BuildMessageNotification()
                {
                    Message = message
                });
        }

        private void AddPendingOutputToActiveSection()
        {
            if (outputSectionText.Length < 1)
                return;

            activeSection.Output += outputSectionText.ToString();
            activeSection.CalculateOutputLength();
            outputSectionText.Clear();
        }

        private async Task SendMessage(RealTimeBuildMessage message)
        {
            await socket.Write(message, CancellationToken.None);
        }
    }
}
