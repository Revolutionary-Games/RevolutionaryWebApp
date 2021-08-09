namespace ThriveDevCenter.Server.Jobs
{
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.Extensions.Logging;

    [DisableConcurrentExecution(60)]
    public class CheckPullRequestCLAJob
    {
        private readonly ILogger<CheckPullRequestCLAJob> logger;

        public CheckPullRequestCLAJob(ILogger<CheckPullRequestCLAJob> logger)
        {
            this.logger = logger;
        }

        public Task Execute(string repository, string pullRequest, string commit, string githubUsername,
            CancellationToken cancellationToken)
        {
            // TODO: implement this checking
            return Task.CompletedTask;
        }
    }
}
