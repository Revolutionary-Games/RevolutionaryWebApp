namespace ThriveDevCenter.Server.Jobs
{
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    public class CheckPullRequestCLAJob
    {
        private readonly ILogger<CheckPullRequestCLAJob> logger;

        public CheckPullRequestCLAJob(ILogger<CheckPullRequestCLAJob> logger)
        {
            this.logger = logger;
        }

        public Task Execute(string commit, string authorEmail, CancellationToken cancellationToken)
        {
            // TODO: implement this checking
            return Task.CompletedTask;
        }
    }
}
