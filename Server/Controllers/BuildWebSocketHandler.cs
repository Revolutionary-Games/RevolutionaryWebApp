namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
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
    using Shared;
    using Shared.Models;
    using Shared.Notifications;
    using Utilities;

    public class BuildWebSocketHandler
    {
        private const int MaxSingleMessageLength = AppInfo.MEBIBYTE * 20;

        /// <summary>
        ///   Not every log message is written to the DB to save on performance
        /// </summary>
        private static readonly TimeSpan OutputSaveInterval = TimeSpan.FromSeconds(15);

        private readonly ILogger<BuildWebSocketHandler> logger;
        private readonly WebSocket socket;
        private readonly NotificationsEnabledDb database;
        private readonly IHubContext<NotificationsHub, INotifications> notifications;
        private readonly CiJob job;

        private readonly string notificationGroup;
        private readonly StringBuilder outputSectionText = new();

        private int sectionNumberCounter;
        private CiJobOutputSection activeSection;

        private DateTime lastSaved = DateTime.UtcNow;

        private BuildWebSocketHandler(ILogger<BuildWebSocketHandler> logger, WebSocket socket,
            NotificationsEnabledDb database, IHubContext<NotificationsHub, INotifications> notifications, CiJob job)
        {
            this.logger = logger;
            this.socket = socket;
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
                    .WhereHashed(nameof(CiJob.BuildOutputConnectKey), keyRaw[0])
                    .AsAsyncEnumerable().FirstOrDefaultAsync(b => b.BuildOutputConnectKey == key);

                if (job == null || job.State == CIJobState.Finished)
                {
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
            var messageSizeBuffer = new byte [4];

            byte[] messageBuffer = null;

            while (!socket.CloseStatus.HasValue)
            {
                var sizeReadResult = await
                    socket.ReceiveAsync(new ArraySegment<byte>(messageSizeBuffer), CancellationToken.None);

                if (sizeReadResult.CloseStatus.HasValue)
                    break;

                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(messageSizeBuffer);

                var messageSize = BitConverter.ToInt32(messageSizeBuffer);

                if (messageSize > MaxSingleMessageLength)
                {
                    logger.LogError("Received too long realTimeBuildMessage length: {MessageSize}", messageSize);
                    await SendMessage(new Error()
                    {
                        ErrorMessage =
                            "Too long realTimeBuildMessage, can't receive, stopping realTimeBuildMessage processing"
                    });

                    break;
                }

                if (messageSize <= 0)
                    continue;

                // Read the realTimeBuildMessage
                // First allocate big enough buffer
                if (messageBuffer == null || messageBuffer.Length < messageSize)
                {
                    messageBuffer = new byte[Math.Min((int)(messageSize * 1.5f), MaxSingleMessageLength)];
                }

                // TODO: can be actually receive a partial amount of the data here? so should we loop until
                // messageSize has been received?
                var readResult =
                    await socket.ReceiveAsync(new ArraySegment<byte>(messageBuffer), CancellationToken.None);

                if (readResult.CloseStatus.HasValue)
                    break;

                if (readResult.Count != messageSize)
                {
                    logger.LogError(
                        "Read realTimeBuildMessage length doesn't match reported length: {MessageSize} actual: {Count}",
                        messageSize, readResult.Count);
                    await SendMessage(new Error()
                    {
                        ErrorMessage =
                            "RealTimeBuildMessage read and reported size mismatch, stopping " +
                            "realTimeBuildMessage processing"
                    });

                    break;
                }

                RealTimeBuildMessage message;
                try
                {
                    message = JsonSerializer.Deserialize<RealTimeBuildMessage>(Encoding.UTF8.GetString(
                        messageBuffer, 0, readResult.Count));

                    if (message == null)
                        throw new NullReferenceException("parsed realTimeBuildMessage is null");
                }
                catch (Exception e)
                {
                    logger.LogError("Failed to parse a received realTimeBuildMessage: {@E}", e);
                    await SendMessage(new Error()
                    {
                        ErrorMessage = "Can't process realTimeBuildMessage, invalid format or content"
                    });
                    continue;
                }

                var now = DateTime.UtcNow;

                switch (message.Type)
                {
                    case BuildSectionMessageType.SectionStart:
                    {
                        // Start a new section
                        if (activeSection != null)
                        {
                            logger.LogError("Received a build output section start while there's an active section");
                            await SendMessage(new Error()
                            {
                                ErrorMessage = "Can't start a new section while one is in progress"
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
                            await SendMessage(new Error()
                            {
                                ErrorMessage = "No active output section"
                            });
                        }
                        else
                        {
                            // Append to current section
                            outputSectionText.Append(message.Output);

                            // Save if time to do so
                            if (now - lastSaved > OutputSaveInterval)
                            {
                                lastSaved = now;
                                AddPendingOutputToActiveSection();

                                await database.SaveChangesAsync();
                            }

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
                            await SendMessage(new Error()
                            {
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

        private async Task SendMessage(object message)
        {
            var buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

            var lengthBuffer = BitConverter.GetBytes(Convert.ToInt32(buffer.Length));

            if (!BitConverter.IsLittleEndian)
                Array.Reverse(lengthBuffer);

            await socket.SendAsync(lengthBuffer, WebSocketMessageType.Binary, false, CancellationToken.None);
            await socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private class Error
        {
            public string ErrorMessage { get; set; }
        }
    }
}
