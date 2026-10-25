namespace RevolutionaryWebApp.Server.Services;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharedBase.Utilities;

/// <summary>
///   External Git and GitHub operations used by translation synchronization jobs.
/// </summary>
public class TranslationRepositoryOperations
{
    public virtual async Task<string> PrepareCheckout(string repository, string branch, string checkout,
        CancellationToken cancellationToken)
    {
        await GitRunHelpers.EnsureRepoIsCloned($"https://github.com/{repository}.git", checkout, true,
            cancellationToken);
        await GitRunHelpers.FetchRef(checkout, $"refs/heads/{branch}", cancellationToken);
        await GitRunHelpers.Checkout(checkout, $"origin/{branch}", true, cancellationToken, true);
        await GitRunHelpers.Clean(checkout, cancellationToken);
        return await GitRunHelpers.GetCurrentCommit(checkout, cancellationToken);
    }

    public virtual ITranslationGithubClient CreateGithubClient(string token)
    {
        return new TranslationGithubClient(token);
    }

    public virtual Task CheckoutNewBranch(string checkout, string branch, string sourceBranch,
        CancellationToken cancellationToken)
    {
        return GitRunHelpers.CheckoutNewBranch(checkout, branch, $"origin/{sourceBranch}", true,
            cancellationToken);
    }

    public virtual Task<string> GetCurrentCommit(string checkout, CancellationToken cancellationToken)
    {
        return GitRunHelpers.GetCurrentCommit(checkout, cancellationToken);
    }

    public virtual async Task CommitFile(string checkout, string path, string message,
        Dictionary<string, string> environment, CancellationToken cancellationToken)
    {
        await GitRunHelpers.Add(checkout, path, cancellationToken);
        await GitRunHelpers.Commit(checkout, message, path, environment, cancellationToken, noVerify: true);
    }

    public virtual Task PushBranch(string checkout, string remote, string branch, string expected,
        Dictionary<string, string> environment, CancellationToken cancellationToken)
    {
        return GitRunHelpers.PushBranch(checkout, remote, branch, expected, environment, cancellationToken);
    }
}
