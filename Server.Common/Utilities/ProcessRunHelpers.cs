namespace ThriveDevCenter.Server.Common.Utilities
{
    using System;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public static class ProcessRunHelpers
    {
        public static Task<ProcessResult> RunProcessAsync(ProcessStartInfo startInfo,
            CancellationToken cancellationToken, bool captureOutput = true)
        {
            if (captureOutput)
            {
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
            }

            var result = new ProcessResult();
            var taskCompletionSource = new TaskCompletionSource<ProcessResult>();

            var process = new Process()
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.Exited += (sender, args) =>
            {
                result.ExitCode = process.ExitCode;
                process.Dispose();
                taskCompletionSource.SetResult(result);
            };

            // TODO: should probably add some timer based cancellation check

            if (captureOutput)
            {
                process.OutputDataReceived += (sender, args) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        taskCompletionSource.SetCanceled(cancellationToken);
                        process.Kill();
                    }

                    result.StdOut.Append(args.Data ?? "");
                };
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        taskCompletionSource.SetCanceled(cancellationToken);
                        process.Kill();
                    }

                    result.StdOut.Append(args.Data ?? "");
                };
            }

            if (!process.Start())
                throw new InvalidOperationException($"Could not start process: {process}");

            if (captureOutput)
            {
                StartProcessOutputRead(process);
            }

            return taskCompletionSource.Task;
        }

        public static void StartProcessOutputRead(Process process)
        {
            // For some reason it seems that this sometimes fails with "System.InvalidOperationException:
            // StandardOut has not been redirected or the process hasn't started yet." So this is retried a
            // few times
            bool success = false;
            for (int i = 0; i < 3; ++i)
            {
                try
                {
                    process.BeginOutputReadLine();
                    success = true;
                    break;
                }
                catch (InvalidOperationException)
                {
                    Thread.Yield();
                }
            }

            if (!success)
                throw new InvalidOperationException("Failed to BeginOutputReadLine even after a few retries");

            process.BeginErrorReadLine();
        }

        public class ProcessResult
        {
            public int ExitCode { get; set; }

            public StringBuilder StdOut { get; set; } = new();
            public StringBuilder ErrorOut { get; set; } = new();

            public string Output => StdOut.ToString();

            public string FullOutput
            {
                get
                {
                    if (ErrorOut.Length < 1)
                        return StdOut.ToString();

                    return StdOut.ToString() + "\n" + ErrorOut.ToString();
                }
            }
        }
    }
}
