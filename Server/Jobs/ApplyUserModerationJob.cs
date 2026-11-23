namespace RevolutionaryWebApp.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Services;

public class ApplyUserModerationJob
{
    private readonly ModerationService moderation;

    public ApplyUserModerationJob(ModerationService moderation)
    {
        this.moderation = moderation;
    }

    public Task Execute(long actionId, bool cleanup, CancellationToken cancellationToken)
    {
        return moderation.ProcessPendingAction(actionId, cleanup, cancellationToken);
    }
}
