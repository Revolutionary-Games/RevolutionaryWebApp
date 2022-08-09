namespace ThriveDevCenter.Server.Common.Utilities;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public static class GitRunHelpers
{
    private const string PullRequestRefSuffix = "/head";
    private const string NormalRefPrefix = "refs/heads/";

    public static async Task EnsureRepoIsCloned(string repoURL, string folder, bool skipLFS,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(FindGit()) { CreateNoWindow = true };

        if (skipLFS)
            SetLFSSmudgeSkip(startInfo);

        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(folder) ??
                throw new Exception("Could not get parent folder to put the repository in"));

            // Need to clone
            startInfo.ArgumentList.Add("clone");
            startInfo.ArgumentList.Add(repoURL);
            startInfo.ArgumentList.Add(folder);
        }
        else
        {
            // Just update remote
            startInfo.WorkingDirectory = folder;
            startInfo.ArgumentList.Add("remote");
            startInfo.ArgumentList.Add("set-url");
            startInfo.ArgumentList.Add("origin");
            startInfo.ArgumentList.Add(repoURL);
        }

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new Exception(
                $"Failed to make sure repo is cloned, process exited with error: {result.FullOutput}");
        }
    }

    public static async Task Checkout(string folder, string whatToCheckout, bool skipLFS,
        CancellationToken cancellationToken, bool force = false)
    {
        var startInfo = PrepareToRunGit(folder, skipLFS);
        startInfo.ArgumentList.Add("checkout");
        startInfo.ArgumentList.Add(whatToCheckout);

        if (force)
            startInfo.ArgumentList.Add("--force");

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new Exception(
                $"Failed to checkout in repo, process exited with error: {result.FullOutput}");
        }
    }

    public static async Task UpdateSubmodules(string folder, bool init, CancellationToken cancellationToken)
    {
        var startInfo = PrepareToRunGit(folder, false);
        startInfo.ArgumentList.Add("submodule");
        startInfo.ArgumentList.Add("update");

        if (init)
            startInfo.ArgumentList.Add("--init");

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new Exception(
                $"Failed to update submodules in repo, process exited with error: {result.FullOutput}");
        }
    }

    public static async Task Pull(string folder, bool skipLFS, CancellationToken cancellationToken,
        bool force = false)
    {
        var startInfo = PrepareToRunGit(folder, skipLFS);
        startInfo.ArgumentList.Add("pull");

        if (force)
            startInfo.ArgumentList.Add("--force");

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new Exception(
                $"Failed to pull in repo, process exited with error: {result.FullOutput}");
        }
    }

    public static async Task Fetch(string folder, bool all, CancellationToken cancellationToken)
    {
        var startInfo = PrepareToRunGit(folder, true);
        startInfo.ArgumentList.Add("fetch");

        if (all)
            startInfo.ArgumentList.Add("--all");

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new Exception(
                $"Failed to fetch in repo, process exited with error: {result.FullOutput}");
        }
    }

    public static async Task Fetch(string folder, string thing, string remote, CancellationToken cancellationToken,
        bool force = true)
    {
        var startInfo = PrepareToRunGit(folder, true);
        startInfo.ArgumentList.Add("fetch");
        startInfo.ArgumentList.Add(remote);
        startInfo.ArgumentList.Add(thing);

        if (force)
            startInfo.ArgumentList.Add("--force");

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new Exception(
                $"Failed to fetch (thing) in repo, process exited with error: {result.FullOutput}");
        }
    }

    /// <summary>
    ///   Gets the current commit in a git repository in the long form
    /// </summary>
    /// <param name="folder">The repository folder</param>
    /// <param name="cancellationToken">Cancellation token for the git process</param>
    /// <param name="attempts">
    ///   How many times to attempt getting the commit. This seems to spuriously fail with 0 exit code and
    ///   no output so this parameter guards against that.
    /// </param>
    /// <returns>The current commit hash</returns>
    /// <exception cref="Exception">If getting the commit hash fails</exception>
    public static async Task<string> GetCurrentCommit(string folder, CancellationToken cancellationToken,
        int attempts = 4)
    {
        int i = 0;
        while (true)
        {
            if (i > 0)
                await Task.Delay(TimeSpan.FromSeconds(i), cancellationToken);

            ++i;

            var startInfo = PrepareToRunGit(folder, true);
            startInfo.ArgumentList.Add("rev-parse");

            // Try to force it being shown as a hash
            startInfo.ArgumentList.Add("--verify");
            startInfo.ArgumentList.Add("HEAD");

            var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);
            if (result.ExitCode != 0)
            {
                if (i < attempts)
                    continue;

                throw new Exception(
                    $"Failed to run rev-parse in repo, process exited with error: {result.FullOutput}");
            }

            var resultText = result.Output.Trim();

            if (string.IsNullOrEmpty(resultText))
            {
                if (i < attempts)
                    continue;

                throw new Exception(
                    $"Failed to run rev-parse in repo, empty output (code: {result.ExitCode}). " +
                    $"Error output (if any): {result.ErrorOut}, normal output: {result.Output}");
            }

            // Looks like sometimes the result is truncated hash, try to detect that here and fail
            if (resultText.Length < 20)
            {
                if (i < attempts)
                    continue;

                throw new Exception(
                    $"Failed to run rev-parse in repo, output is not full hash length (code: {result.ExitCode}). " +
                    $"Error output (if any): {result.ErrorOut}, normal output: {result.Output}");
            }

            return resultText;
        }
    }

    /// <summary>
    ///   Handles the differences between checking a github PR or just a remote ref branch
    /// </summary>
    /// <param name="folder">The fit folder to operate in</param>
    /// <param name="refToCheckout">Ref from Github that should be checked out locally</param>
    /// <param name="skipLFS">If true LFS handling is skipped</param>
    /// <param name="cancellationToken">Cancel the operation early</param>
    /// <remarks>
    ///   <para>
    ///     If this is updated "ci_executor.rb" needs also know how to checkout the new things
    ///   </para>
    /// </remarks>
    public static async Task SmartlyCheckoutRef(string folder, string refToCheckout, bool skipLFS,
        CancellationToken cancellationToken)
    {
        const string remote = "origin";
        var parsed = ParseRemoteRef(refToCheckout, remote);

        if (IsPullRequestRef(refToCheckout))
        {
            await Fetch(folder, $"{refToCheckout}:{parsed.localBranch}", remote, cancellationToken);
        }
        else
        {
            await Fetch(folder, refToCheckout, remote, cancellationToken);
        }

        await Checkout(folder, parsed.localRef, skipLFS, cancellationToken, true);
    }

    public static async Task FetchRef(string folder, string refToFetch, CancellationToken cancellationToken)
    {
        const string remote = "origin";
        var parsed = ParseRemoteRef(refToFetch, remote);

        if (IsPullRequestRef(refToFetch))
        {
            await Fetch(folder, $"{refToFetch}:{parsed.localBranch}", remote, cancellationToken);
        }
        else
        {
            await Fetch(folder, refToFetch, remote, cancellationToken);
        }
    }

    public static async Task<string> Clean(string folder, CancellationToken cancellationToken,
        bool removeUntrackedDirectories = true)
    {
        if (!Directory.Exists(folder))
            throw new ArgumentException($"Specified folder: \"{folder}\" doesn't exist");

        var startInfo = new ProcessStartInfo(FindGit()) { CreateNoWindow = true, WorkingDirectory = folder };

        startInfo.ArgumentList.Add("clean");
        startInfo.ArgumentList.Add("-f");

        if (removeUntrackedDirectories)
            startInfo.ArgumentList.Add("-d");

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new Exception(
                $"Failed to clean repo, process exited with error: {result.FullOutput}");
        }

        return result.Output;
    }

    public static bool IsPullRequestRef(string remoteRef)
    {
        if (remoteRef.StartsWith("pull/"))
            return true;

        return false;
    }

    public static string GenerateRefForPullRequest(long id)
    {
        return $"pull/{id}/head";
    }

    public static (string localBranch, string localRef) ParseRemoteRef(string remoteRef, string remote = "origin")
    {
        string localHeadsRef = $"refs/remotes/{remote}/";

        if (IsPullRequestRef(remoteRef))
        {
            if (remoteRef.EndsWith(PullRequestRefSuffix))
            {
                var localBranch = remoteRef.Substring(0, remoteRef.Length - PullRequestRefSuffix.Length);
                localHeadsRef += localBranch;
                return (localBranch, localHeadsRef);
            }

            throw new Exception($"Unrecognized PR ref: {remoteRef}");
        }

        if (remoteRef.StartsWith(NormalRefPrefix))
        {
            var localBranch = remoteRef.Substring(NormalRefPrefix.Length);
            localHeadsRef += localBranch;
            return (localBranch, localHeadsRef);
        }

        throw new Exception($"Unrecognized normal ref: {remoteRef}");
    }

    public static string ParseRefBranch(string remoteRef)
    {
        return ParseRemoteRef(remoteRef).localBranch;
    }

    private static ProcessStartInfo PrepareToRunGit(string folder, bool skipLFS)
    {
        if (!Directory.Exists(folder))
            throw new ArgumentException($"Specified folder: \"{folder}\" doesn't exist");

        var startInfo = new ProcessStartInfo(FindGit()) { CreateNoWindow = true };

        if (skipLFS)
            SetLFSSmudgeSkip(startInfo);

        startInfo.WorkingDirectory = folder;
        return startInfo;
    }

    private static void SetLFSSmudgeSkip(ProcessStartInfo startInfo)
    {
        startInfo.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";
    }

    private static string FindGit()
    {
        var git = ExecutableFinder.Which("git");

        if (git == null)
            throw new Exception("Git executable not found");

        return git;
    }
}
