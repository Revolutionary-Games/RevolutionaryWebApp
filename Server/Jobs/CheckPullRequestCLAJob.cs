namespace ThriveDevCenter.Server.Jobs
{
    using System.Threading;
    using System.Threading.Tasks;

    public class CheckPullRequestCLAJob
    {
        public CheckPullRequestCLAJob()
        {

        }

        public Task Execute(string commit, string authorEmail, CancellationToken cancellationToken)
        {
            // TODO: implement this checking
            return Task.CompletedTask;
        }
    }
}
