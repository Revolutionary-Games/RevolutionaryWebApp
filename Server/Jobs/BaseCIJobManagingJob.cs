namespace RevolutionaryWebApp.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Utilities;

public abstract class BaseCIJobManagingJob
{
    protected readonly ILogger<BaseCIJobManagingJob> Logger;
    protected readonly NotificationsEnabledDb Database;
    protected readonly IGithubCommitStatusReporter StatusReporter;
    protected readonly IBackgroundJobClient JobClient;

    protected BaseCIJobManagingJob(ILogger<BaseCIJobManagingJob> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient, IGithubCommitStatusReporter statusReporter)
    {
        Logger = logger;
        Database = database;
        JobClient = jobClient;
        StatusReporter = statusReporter;
    }

    protected async Task OnJobEnded(CiJob job)
    {
        // After running the job, the changes saving should not be skipped
        await Database.SaveChangesAsync();

        // Send status to GitHub
        var status = GithubAPI.CommitStatus.Success;
        string statusDescription = "Checks succeeded";

        if (!job.Succeeded)
        {
            status = GithubAPI.CommitStatus.Failure;
            statusDescription = "Some checks failed";
        }

        if (job.Build?.CiProject == null)
            throw new NotLoadedModelNavigationException();

        if (!await StatusReporter.SetCommitStatus(job.Build.CiProject.RepositoryFullName, job.Build.CommitHash,
                status, StatusReporter.CreateStatusUrlForJob(job), statusDescription,
                job.JobName))
        {
            Logger.LogError("Failed to set commit status for build's job: {JobName}", job.JobName);
        }

        JobClient.Enqueue<CheckOverallBuildStatusJob>(x =>
            x.Execute(job.CiProjectId, job.CiBuildId, CancellationToken.None));
    }
}
