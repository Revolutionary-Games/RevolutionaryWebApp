namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Logging;
using Models;

public class CloseAutoClosePollJob
{
    private readonly ILogger<CloseAutoClosePollJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public CloseAutoClosePollJob(ILogger<CloseAutoClosePollJob> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(long meetingId, long pollId, CancellationToken cancellationToken)
    {
        var poll = await database.MeetingPolls.FindAsync(meetingId, pollId);

        if (poll == null)
        {
            logger.LogError("Can't auto-close a non-existent poll: {MeetingId}-{PollId}", meetingId, pollId);
            return;
        }

        if (poll.AutoCloseAt == null)
        {
            logger.LogInformation("Auto-close poll is no longer an auto-close, skipping doing anything");
            return;
        }

        // If not time yet, reschedule
        if (poll.AutoCloseAt.Value > DateTime.UtcNow)
        {
            logger.LogWarning("Auto-close poll close time is in the future, scheduling a new job to run then");
            jobClient.Schedule<CloseAutoClosePollJob>(x => x.Execute(meetingId, pollId, CancellationToken.None),
                poll.AutoCloseAt.Value);
            return;
        }

        // Don't even log anything if already closed
        if (poll.ClosedAt != null)
            return;

        poll.ClosedAt = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Auto-closed poll: {MeetingId}-{PollId}", meetingId, pollId);

        // Queue a job to calculate the results
        jobClient.Enqueue<ComputePollResultsJob>(x => x.Execute(meetingId, pollId,
            CancellationToken.None));
    }
}
