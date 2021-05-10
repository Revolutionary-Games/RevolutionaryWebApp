namespace ThriveDevCenter.Server.Utilities
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire.Common;

    public static class GitRunHelpers
    {
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
            if (!Directory.Exists(folder))
                throw new ArgumentException($"Specified folder: \"{folder}\" doesn't exist");

            var startInfo = new ProcessStartInfo(FindGit()) { CreateNoWindow = true };
            SetLFSSmudgeSkip(startInfo);
            startInfo.WorkingDirectory = folder;

            startInfo.ArgumentList.Add("checkout");
            startInfo.ArgumentList.Add(whatToCheckout);

            if (force)
                startInfo.ArgumentList.Add("--force");

            var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new Exception(
                    $"Failed to make checkout in repo, process exited with error: {result.FullOutput}");
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
                    $"Failed to make checkout in repo, process exited with error: {result.FullOutput}");
            }
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
