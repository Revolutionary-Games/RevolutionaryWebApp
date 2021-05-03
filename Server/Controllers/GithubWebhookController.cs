using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System.ComponentModel.DataAnnotations;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;

    [ApiController]
    [Route("api/v1/webhook/github")]
    public class GithubWebhookController : Controller
    {
        private readonly ILogger<GithubWebhookController> logger;
        private readonly ApplicationDbContext database;

        public GithubWebhookController(ILogger<GithubWebhookController> logger, ApplicationDbContext database)
        {
            this.logger = logger;
            this.database = database;
        }

        [HttpPost()]
        public async Task<IActionResult> ReceiveWebhook([FromForm] [Required] GithubWebhookRequest request)
        {
            logger.LogInformation("Received a github webhook with payload size: {Length}", request.Payload.Length);

            var hook = await database.GithubWebhooks.FindAsync(AppInfo.SingleResourceTableRowId);

            if (hook == null)
            {
                logger.LogWarning("Github webhook secret is not configured, can't process webhook");
                return BadRequest("Incorrect secret");
            }

            return Ok();
        }
    }

    public class GithubWebhookRequest
    {
        [Required]
        public string Payload { get; set; }
    }
}
