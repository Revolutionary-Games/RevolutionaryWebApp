namespace ThriveDevCenter.Server.Common.Utilities;

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class ProcessRunHelpers
{
    public static Task<ProcessResult> RunProcessAsync(ProcessStartInfo startInfo,
        CancellationToken cancellationToken, bool captureOutput = true, int startRetries = 5)
    {
        if (captureOutput)
        {
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
        }

        try
        {
            return StartProcessInternal(startInfo, cancellationToken, captureOutput).Task;
        }
        catch (InvalidOperationException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (startRetries > 0)
            {
                return RunProcessAsync(startInfo, cancellationToken, captureOutput, startRetries - 1);
            }

            throw;
        }
    }

    public static void StartProcessOutputRead(Process process, CancellationToken cancellationToken)
    {
        if (process == null)
            throw new ArgumentException("Process must not be null for starting output read");

        const int retries = 5;

        // For some reason it seems that this sometimes fails with "System.InvalidOperationException:
        // StandardOut has not been redirected or the process hasn't started yet." So this is retried a
        // few times
        bool success = false;
        for (int i = 0; i < retries; ++i)
        {
            try
            {
                process.BeginOutputReadLine();
                success = true;
                break;
            }
            catch (Exception e) when (e is InvalidOperationException or NullReferenceException)
            {
                if (cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(15 * (i + 1))))
                    cancellationToken.ThrowIfCancellationRequested();
            }
        }

        if (!success)
            throw new InvalidOperationException("Failed to BeginOutputReadLine even after a few retries");

        cancellationToken.ThrowIfCancellationRequested();

        success = false;
        for (int i = 0; i < retries; ++i)
        {
            try
            {
                process.BeginErrorReadLine();
                success = true;
                break;
            }
            catch (Exception e) when (e is InvalidOperationException or NullReferenceException)
            {
                if (cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(15 * (i + 1))))
                    cancellationToken.ThrowIfCancellationRequested();
            }
        }

        if (!success)
            throw new InvalidOperationException("Failed to BeginErrorReadLine even after a few retries");
    }

    private static TaskCompletionSource<ProcessResult> StartProcessInternal(ProcessStartInfo startInfo,
        CancellationToken cancellationToken, bool captureOutput)
    {
        var result = new ProcessResult();
        var taskCompletionSource = new TaskCompletionSource<ProcessResult>();

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        process.Exited += (_, _) =>
        {
            result.ExitCode = process.ExitCode;
            process.Dispose();
            taskCompletionSource.SetResult(result);
        };

        // TODO: should probably add some timer based cancellation check

        if (captureOutput)
        {
            process.OutputDataReceived += (_, args) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    taskCompletionSource.SetCanceled(cancellationToken);
                    process.Kill();
                }

                if (args.Data == null)
                    return;

                result.StdOut.Append($"{args.Data}\n");
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    taskCompletionSource.SetCanceled(cancellationToken);
                    process.Kill();
                }

                if (args.Data == null)
                    return;

                result.ErrorOut.Append($"{args.Data}\n");
            };
        }

        if (!process.Start())
            throw new InvalidOperationException($"Could not start process: {process}");

        if (captureOutput)
        {
            StartProcessOutputRead(process, cancellationToken);
        }

        return taskCompletionSource;
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

                return StdOut + "\n" + ErrorOut;
            }
        }
    }
}