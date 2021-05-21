namespace ThriveDevCenter.Server.Utilities
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public static class GitRunHelpers
    {
        private const string PullRequestRefSuffix = "/head";
        private const string NormalRefPrefix = "refs/heads/";

        public static async Task EnsureRepoIsCloned(string repoURL, string folder, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo(FindGit()) { CreateNoWindow = true };
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

        public static async Task Checkout(string folder, string whatToCheckout, CancellationToken cancellationToken,
            bool force = false)
        {
            var startInfo = PrepareToRunGit(folder);
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

        public static async Task Fetch(string folder, bool all, CancellationToken cancellationToken)
        {
            var startInfo = PrepareToRunGit(folder);
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

        public static async Task Fetch(string folder, string thing, string remote, CancellationToken cancellationToken)
        {
            var startInfo = PrepareToRunGit(folder);
            startInfo.ArgumentList.Add("fetch");
            startInfo.ArgumentList.Add(remote);
            startInfo.ArgumentList.Add(thing);

            var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new Exception(
                    $"Failed to fetch (thing) in repo, process exited with error: {result.FullOutput}");
            }
        }

        /// <summary>
        ///   Handles the differences between checking a github PR or just a remote ref branch
        /// </summary>
        /// <param name="folder">The fit folder to operate in</param>
        /// <param name="refToCheckout">Ref from Github that should be checked out locally</param>
        /// <param name="cancellationToken">Cancel the operation early</param>
        /// <remarks>
        ///   <para>
        ///     If this is updated "ci_executor.rb" needs also know how to checkout the new things
        ///   </para>
        /// </remarks>
        public static async Task SmartlyCheckoutRef(string folder, string refToCheckout,
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

            await Checkout(folder, parsed.localRef, cancellationToken, true);
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

        public static async Task Clean(string folder, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(folder))
                throw new ArgumentException($"Specified folder: \"{folder}\" doesn't exist");

            var startInfo = new ProcessStartInfo(FindGit()) { CreateNoWindow = true };
            SetLFSSmudgeSkip(startInfo);
            startInfo.WorkingDirectory = folder;

            startInfo.ArgumentList.Add("clean");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("-d");

            var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new Exception(
                    $"Failed to clean repo, process exited with error: {result.FullOutput}");
            }
        }

        public static bool IsPullRequestRef(string remoteRef)
        {
            if (remoteRef.StartsWith("pull/"))
                return true;

            return false;
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
            else
            {
                if (remoteRef.StartsWith(NormalRefPrefix))
                {
                    var localBranch = remoteRef.Substring(NormalRefPrefix.Length);
                    localHeadsRef += localBranch;
                    return (localBranch, localHeadsRef);
                }

                throw new Exception($"Unrecognized normal ref: {remoteRef}");
            }
        }

        public static string ParseRefBranch(string remoteRef)
        {
            return ParseRemoteRef(remoteRef).localBranch;
        }

        private static ProcessStartInfo PrepareToRunGit(string folder)
        {
            if (!Directory.Exists(folder))
                throw new ArgumentException($"Specified folder: \"{folder}\" doesn't exist");

            var startInfo = new ProcessStartInfo(FindGit()) { CreateNoWindow = true };
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
}
