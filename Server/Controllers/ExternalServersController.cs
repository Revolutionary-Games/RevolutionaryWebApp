using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Authorization;
    using BlazorPagination;
    using Filters;
    using Hangfire;
    using Jobs;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Renci.SshNet.Common;
    using Services;
    using Shared;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class ExternalServersController : Controller
    {
        private readonly ILogger<ExternalServersController> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IExternalServerSSHAccess serverSSHAccess;
        private readonly IBackgroundJobClient jobClient;

        public ExternalServersController(ILogger<ExternalServersController> logger, NotificationsEnabledDb database,
            IExternalServerSSHAccess serverSSHAccess, IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.serverSSHAccess = serverSSHAccess;
            this.jobClient = jobClient;
        }

        [HttpGet]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<ActionResult<PagedResult<ExternalServerDTO>>> Get([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 100)] int pageSize)
        {
            IQueryable<ExternalServer> query;

            try
            {
                query = database.ExternalServers.OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetDTO());
        }

        [HttpGet("{id:long}")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<ActionResult<ExternalServerDTO>> GetSingle(long id)
        {
            var server = await database.ExternalServers.FindAsync(id);

            if (server == null)
                return NotFound();

            return server.GetDTO();
        }

        [HttpPost]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<IActionResult> Create([Required] [FromBody] ExternalServerDTO request)
        {
            FailIfNotConfigured();

            if (request.PublicAddress == null)
                return BadRequest("Missing address");

            if (string.IsNullOrEmpty(request.SSHKeyFileName) || request.SSHKeyFileName.Contains("..") ||
                request.SSHKeyFileName.Contains("/"))
            {
                return BadRequest("Invalid SSH key name format");
            }

            if (!serverSSHAccess.IsValidKey(request.SSHKeyFileName))
                return BadRequest("Invalid SSH key specified");

            // Test connection
            try
            {
                serverSSHAccess.ConnectTo(request.PublicAddress.ToString(), request.SSHKeyFileName);
            }
            catch (Exception e)
            {
                logger.LogWarning("Failing to add a new external server due to connect failure: {@E}", e);
                return BadRequest("Can't access the specified IP address with the specified key");
            }

            // Don't allow duplicate IPs
            if (await database.ExternalServers.FirstOrDefaultAsync(s =>
                s.PublicAddress.Equals(request.PublicAddress)) != null)
            {
                return BadRequest("There is already a server configured with that IP address");
            }

            var server = new ExternalServer()
            {
                PublicAddress = request.PublicAddress,
                SSHKeyFileName = request.SSHKeyFileName,
            };
            await database.ExternalServers.AddAsync(server);

            await database.AdminActions.AddAsync(new AdminAction()
            {
                Message = $"New external server with IP {request.PublicAddress} added",
                PerformedById = HttpContext.AuthenticatedUser().Id,
            });

            await database.SaveChangesAsync();

            jobClient.Enqueue<ProvisionExternalServerJob>(x => x.Execute(server.Id, CancellationToken.None));

            return Ok();
        }

        [HttpDelete("{id:long}")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<IActionResult> Destroy(long id)
        {
            FailIfNotConfigured();

            var server = await database.ExternalServers.FindAsync(id);

            if (server == null)
                return NotFound();

            if (server.Status != ServerStatus.Stopped)
                return BadRequest("Only stopped server can be deleted");

            var user = HttpContext.AuthenticatedUser();

            await database.AdminActions.AddAsync(new AdminAction()
            {
                Message = $"External server {id} deleted",
                PerformedById = user.Id,
            });

            database.ExternalServers.Remove(server);
            await database.SaveChangesAsync();

            logger.LogInformation("External server {Id} deleted by {Email}", server.Id, user.Email);

            return Ok();
        }

        [HttpPost("{id:long}/offline")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<IActionResult> MarkOffline(long id, [Required] bool offline)
        {
            FailIfNotConfigured();

            var server = await database.ExternalServers.FindAsync(id);

            if (server == null)
                return NotFound();

            if (offline)
            {
                server.Status = ServerStatus.Stopped;
                await database.AdminActions.AddAsync(new AdminAction()
                {
                    Message = $"External server {id} marked offline",
                    PerformedById = HttpContext.AuthenticatedUser().Id,
                });
            }
            else
            {
                server.Status = ServerStatus.WaitingForStartup;

                await database.AdminActions.AddAsync(new AdminAction()
                {
                    Message = $"External server {id} marked online",
                    PerformedById = HttpContext.AuthenticatedUser().Id,
                });
            }

            server.BumpUpdatedAt();
            await database.SaveChangesAsync();

            if (!offline)
                jobClient.Enqueue<WaitForExternalServerStartUpJob>(x => x.Execute(server.Id, CancellationToken.None));

            return Ok();
        }

        [HttpPost("{id:long}/priority")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<IActionResult> UpdatePriority(long id,
            [Required] [FromBody] [Range(AppInfo.MinExternalServerPriority, AppInfo.MaxExternalServerPriority)]
            int priority)
        {
            var server = await database.ExternalServers.FindAsync(id);

            if (server == null)
                return NotFound();

            if (server.Priority == priority)
                return Ok();

            server.Priority = priority;
            await database.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("{id:long}/reboot")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<IActionResult> ForceRebootServer(long id)
        {
            FailIfNotConfigured();

            var server = await database.ExternalServers.FindAsync(id);

            if (server == null)
                return NotFound();

            if (server.ReservationType != ServerReservationType.None)
            {
                await database.AdminActions.AddAsync(new AdminAction()
                {
                    Message = $"External server {id} force rebooted by an admin while it was reserved",
                    PerformedById = HttpContext.AuthenticatedUser().Id,
                });
            }
            else if (server.Status == ServerStatus.Provisioning)
            {
                await database.AdminActions.AddAsync(new AdminAction()
                {
                    Message = $"External server {id} force rebooted by an admin while it was provisioning",
                    PerformedById = HttpContext.AuthenticatedUser().Id,
                });
            }
            else
            {
                await database.AdminActions.AddAsync(new AdminAction()
                {
                    Message = $"External server {id} rebooted by an admin",
                    PerformedById = HttpContext.AuthenticatedUser().Id,
                });
            }

            try
            {
                serverSSHAccess.ConnectTo(server.PublicAddress.ToString(), server.SSHKeyFileName);
                serverSSHAccess.Reboot();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to reboot external server {Id} due to exception", server.Id);
                return BadRequest("Failed to issue reboot command to target server");
            }

            server.StatusLastChecked = DateTime.UtcNow;
            server.Status = ServerStatus.Stopping;
            UpdateStoppingServerInfo(server);

            await database.SaveChangesAsync();

            jobClient.Schedule<WaitForExternalServerStartUpJob>(x => x.Execute(server.Id, CancellationToken.None),
                TimeSpan.FromSeconds(20));

            return Ok();
        }

        [HttpPost("{id:long}/queueCleanUp")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<IActionResult> QueueCleanUp(long id)
        {
            var server = await database.ExternalServers.FindAsync(id);

            if (server == null)
                return NotFound();

            if (server.CleanUpQueued)
                return Ok("Server already has clean up queued");

            await database.AdminActions.AddAsync(new AdminAction()
            {
                Message = $"Server {id} is queued for clean up",
                PerformedById = HttpContext.AuthenticatedUser().Id,
            });

            server.CleanUpQueued = true;
            server.BumpUpdatedAt();
            await database.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("{id:long}/queueMaintenance")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<IActionResult> QueueMaintenance(long id)
        {
            FailIfNotConfigured();

            var server = await database.ExternalServers.FindAsync(id);

            if (server == null)
                return NotFound();

            if (server.WantsMaintenance)
                return Ok("Server already wants maintenance");

            await database.AdminActions.AddAsync(new AdminAction()
            {
                Message = $"Server {id} is queued for maintenance",
                PerformedById = HttpContext.AuthenticatedUser().Id,
            });

            server.WantsMaintenance = true;
            server.BumpUpdatedAt();
            await database.SaveChangesAsync();

            // TODO: job to do maintenance

            return Ok();
        }

        [HttpPost("{id:long}/refreshStatus")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<IActionResult> RefreshServerStatus(long id)
        {
            FailIfNotConfigured();

            var server = await database.ExternalServers.FindAsync(id);

            if (server == null)
                return NotFound();

            if (server.Status == ServerStatus.Stopping &&
                DateTime.UtcNow - server.StatusLastChecked < TimeSpan.FromMinutes(1))
            {
                return BadRequest("The server is currently stopping (last checked less than minute ago)");
            }

            ServerStatus newStatus = ServerStatus.Stopped;

            try
            {
                serverSSHAccess.ConnectTo(server.PublicAddress.ToString(), server.SSHKeyFileName);
                newStatus = ServerStatus.Running;
            }
            catch (SocketException)
            {
                logger.LogInformation("Connection failed (socket exception), detected server status is stopped");
            }
            catch (SshOperationTimeoutException)
            {
                logger.LogInformation("Connection failed (ssh timed out), detected server status is stopped");
            }

            if (newStatus != server.Status)
            {
                server.Status = newStatus;
                logger.LogInformation("External server {Id} status is now {Status} after status re-check request",
                    server.Id, server.Status);

                server.StatusLastChecked = DateTime.UtcNow;
                server.BumpUpdatedAt();

                if (server.Status != ServerStatus.Running && server.Status != ServerStatus.WaitingForStartup)
                {
                    UpdateStoppingServerInfo(server);
                }

                await database.SaveChangesAsync();
            }

            return Ok();
        }

        [NonAction]
        private static void UpdateStoppingServerInfo(ExternalServer server)
        {
            var now = DateTime.UtcNow;

            server.StatusLastChecked = now;
            server.BumpUpdatedAt();
            server.ReservationType = ServerReservationType.None;
            server.RunningSince = null;
        }

        [NonAction]
        private void FailIfNotConfigured()
        {
            if (!serverSSHAccess.Configured)
            {
                throw new HttpResponseException
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Value = "SSH access is not configured",
                };
            }
        }
    }
}
