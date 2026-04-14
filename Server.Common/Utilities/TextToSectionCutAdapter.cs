namespace RevolutionaryWebApp.Server.Common.Utilities;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Services;
using Shared;
using SharedBase.Utilities;

/// <summary>
///   Handles cutting process output into sections based on special commands in the text data
/// </summary>
public class TextToSectionCutAdapter : IDisposable
{
    public const string OutputSpecialCommandMarker = "#--@%-DevCenter-%@--";
    private const string LineTruncateMessage = " THIS LINE WAS TRUNCATED BECAUSE IT IS TOO LONG";

    private readonly IJobOutputForwarder nextStep;

    private readonly SemaphoreSlim errorLock = new(1, 1);

    /// <summary>
    ///   We may need to buffer error lines until a section opens
    /// </summary>
    private readonly Queue<string> errorLineQueue = new();

    private bool hasOpenSection;

    public TextToSectionCutAdapter(IJobOutputForwarder nextStep)
    {
        this.nextStep = nextStep;
    }

    public bool HadFailure { get; private set; }

    public async Task OnNewJobStarted()
    {
        await nextStep.OnNewJobStarted();
    }

    public async Task Flush(CancellationToken cancellationToken)
    {
        if (!await errorLock.WaitAsync(TimeSpan.FromSeconds(500), cancellationToken))
            return;

        try
        {
            while (errorLineQueue.TryDequeue(out var error))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!hasOpenSection)
                {
                    await nextStep.OpenNewSection("Flushed errors without a section");
                }

                await nextStep.ForwardOutputToActiveSection(error);
            }
        }
        finally
        {
            errorLock.Release();
        }
    }

    public async Task OnProcessOutputLine(string output)
    {
        // -1 is passed here to give line terminator some space
        output = output.Truncate(LineTruncateMessage, AppInfo.MaxBuildOutputLineLength - 1);

        if (output.StartsWith(OutputSpecialCommandMarker, StringComparison.Ordinal))
        {
            var parts = output.Split(' ', 3);

            switch (parts[1])
            {
                case "SectionEnd":
                {
                    if (!int.TryParse(parts[2], out var exitCode))
                    {
                        exitCode = -123;
                    }

                    var success = exitCode == 0;

                    if (!success)
                        HadFailure = true;

                    await nextStep.CloseSection(success);
                    hasOpenSection = false;
                    break;
                }

                case "SectionStart":
                {
                    var sectionName = parts.Length > 2 ? parts[2] : "Unnamed Section";

                    await nextStep.OpenNewSection(sectionName);
                    hasOpenSection = true;
                    break;
                }

                default:
                {
                    try
                    {
                        await OnProcessErrorLine("Unknown special command received from build process")
                            .WaitAsync(TimeSpan.FromSeconds(60));
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("ERROR: could not warn about unknown section command: " + e);
                    }

                    return;
                }
            }

            if (hasOpenSection)
            {
                // Clear the error queue now that we have an active section
                if (!await errorLock.WaitAsync(TimeSpan.FromSeconds(500)))
                {
                    await nextStep.ForwardOutputToActiveSection("Error: Cannot get error queue lock");
                    return;
                }

                try
                {
                    bool first = true;

                    while (errorLineQueue.TryDequeue(out var line))
                    {
                        if (first)
                            await nextStep.ForwardOutputToActiveSection("Following are error lines:\n");

                        await nextStep.ForwardOutputToActiveSection(line);

                        first = false;
                    }
                }
                finally
                {
                    errorLock.Release();
                }
            }
        }
        else
        {
            // As we expect to be called without the line terminator, add one now when forwarding the text
            await nextStep.ForwardOutputToActiveSection(output + "\n");
        }
    }

    public async Task OnProcessErrorLine(string output)
    {
        // As there's less error output, we just directly add the line terminator here at the start
        output = output.Truncate(LineTruncateMessage, AppInfo.MaxBuildOutputLineLength - 1) + "\n";

        if (!hasOpenSection)
        {
            if (!await errorLock.WaitAsync(TimeSpan.FromSeconds(500)))
                throw new Exception("Cannot get error queue lock");

            try
            {
                if (errorLineQueue.Count < 1000)
                {
                    errorLineQueue.Enqueue(output);

                    if (errorLineQueue.Count >= 1000)
                    {
                        errorLineQueue.Enqueue("TOO MANY ERRORS WITHOUT ACTIVE SECTION, TRUNCATING");
                    }
                }
            }
            finally
            {
                errorLock.Release();
            }
        }
        else
        {
            await nextStep.ForwardOutputToActiveSection(output);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            errorLock.Dispose();
        }
    }
}
