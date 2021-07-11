namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;

    public class ComputePollResultsJob
    {
        private readonly ILogger<ComputePollResultsJob> logger;
        private readonly NotificationsEnabledDb database;

        public ComputePollResultsJob(ILogger<ComputePollResultsJob> logger, NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
        }

        public async Task Execute(long meetingId, long pollId, CancellationToken cancellationToken)
        {
            var poll = await database.MeetingPolls.FindAsync(meetingId, pollId);

            if (poll == null)
            {
                logger.LogError("Can't compute results for non-existent poll: {MeetingId}-{PollId}", meetingId, pollId);
                return;
            }

            if (poll.ClosedAt == null)
                throw new Exception("Can't calculate results for a poll that is not closed");

            // This will work until we have more than hundreds of thousands of votes per poll
            var votes = await database.MeetingPollVotes.AsQueryable()
                .Where(v => v.MeetingId == poll.MeetingId && v.PollId == poll.PollId)
                .ToListAsync(cancellationToken);

            poll.CalculateResults(votes);

            await database.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Poll results computed for: {MeetingId}-{PollId}", meetingId, pollId);
        }
    }
}
