using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System.Threading.Tasks;
    using Authorization;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Shared.Models;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class GithubConfigurationController : Controller
    {
        private readonly ILogger<GithubConfigurationController> logger;
        private readonly ApplicationDbContext database;

        public GithubConfigurationController(ILogger<GithubConfigurationController> logger,
            ApplicationDbContext database)
        {
            this.logger = logger;
            this.database = database;
        }

        [HttpGet("viewSecret")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<GithubWebhookDTO> GetSecret()
        {
            logger.LogInformation("Github webhook secret viewed by {Email}", HttpContext.AuthenticatedUser().Email);
            return (await GetOrCreateHook()).GetDTO();
        }

        [HttpPost("recreateSecret")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<GithubWebhookDTO> RecreateSecret()
        {
            var existing = await GetOrCreateHook();

            await database.AdminActions.AddAsync(new AdminAction()
            {
                Message = "Github webhook secret recreated",
                PerformedById = HttpContext.AuthenticatedUser().Id
            });

            existing.CreateSecret();
            await database.SaveChangesAsync();

            return existing.GetDTO();
        }

        [NonAction]
        private async Task<GithubWebhook> GetOrCreateHook()
        {
            var existing = await database.GithubWebhooks.FindAsync(AppInfo.SingleResourceTableRowId);

            if (existing != null)
                return existing;

            await database.AdminActions.AddAsync(new AdminAction()
            {
                Message = "New Github webhook secret created",
                PerformedById = HttpContext.AuthenticatedUser().Id
            });

            var webhook = new GithubWebhook()
            {
                Id = AppInfo.SingleResourceTableRowId,
            };
            webhook.CreateSecret();

            await database.GithubWebhooks.AddAsync(webhook);
            await database.SaveChangesAsync();

            return webhook;
        }
    }
}
