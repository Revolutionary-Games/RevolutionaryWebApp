namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface ITranslationGithubClient : IDisposable
{
    public Task<string> GetLogin(CancellationToken cancellationToken);

    public Task EnsureFork(string sourceRepository, string owner, CancellationToken cancellationToken);

    public Task<TranslationGithubPullRequest?> GetPullRequest(string repository, int number,
        CancellationToken cancellationToken);

    public Task<TranslationGithubPullRequest> CreatePullRequest(string repository, string branch,
        string forkOwner, string headBranch, CancellationToken cancellationToken);
}
