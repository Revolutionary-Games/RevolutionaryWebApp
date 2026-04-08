namespace RevolutionaryWebApp.Server.Common.Services;

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharedBase.Utilities;

/// <summary>
///   Handles sending job output to the client
/// </summary>
public interface IJobOutputForwarder
{
    /// <summary>
    ///   Max length that a section title can be.
    ///   If this is increased, the RealtimeBuildMessage class needs to be updated.
    /// </summary>
    public const int MaxSectionLength = 100;

    public bool HasOpenSection { get; }

    /// <summary>
    ///   Called when a new job is started so that ID sequences can be reset
    /// </summary>
    public Task OnNewJobStarted();

    public Task OpenNewSection(string sectionName);
    public Task CloseSection(bool success);
    public Task ForwardOutputToActiveSection(string output);
}

/// <summary>
///   Forwards output based on action callbacks and ensures only one callback is active at a time. Note that this does
///   not implement safety for the output tasks, so if they can block for some time, the implementation needs to have
///   safety timeouts.
/// </summary>
public sealed class SimpleJobOutputForwarder : IJobOutputForwarder, IDisposable
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    private readonly int targetOutputBatchSize;
    private readonly TimeSpan outputFlushDelay;

    private readonly bool usesImmediateOutput;

    // Callback parameters are the section name and id, and then a type-specific parameter
    // For section close we get a success flag, and for output we get the actual text
    private readonly Func<string, int, bool, Task> onSectionClosed;
    private readonly Func<string, int, Task> onSectionOpened;
    private readonly Func<string, int, string, Task> onSectionOutput;

    private readonly StringBuilder buffer = new();

    private bool cancel;

    private string? openSectionName;
    private int openSectionNumber;

    private bool hashFlushTask;
    private bool flushDone;
    private Task? flushTask;

    private DateTime lastFlushTime = DateTime.MinValue;

    public SimpleJobOutputForwarder(Func<string, int, bool, Task> onSectionClosed,
        Func<string, int, Task> onSectionOpened,
        Func<string, int, string, Task> onSectionOutput, int targetOutputBatchSize = 762, float flushDelay = 1.1f)
    {
        this.targetOutputBatchSize = targetOutputBatchSize;
        this.onSectionClosed = onSectionClosed;
        this.onSectionOpened = onSectionOpened;
        this.onSectionOutput = onSectionOutput;
        outputFlushDelay = TimeSpan.FromSeconds(flushDelay);

        usesImmediateOutput = outputFlushDelay <= TimeSpan.FromMilliseconds(1);
    }

    public bool HasOpenSection => openSectionName != null;

    public void Dispose()
    {
        cancel = true;

        if (!semaphore.Wait(TimeSpan.FromSeconds(5)))
        {
            Console.WriteLine("Output forwarder is still in use when trying to dispose!");
            return;
        }

        flushTask?.Wait();
        flushTask = null;
        semaphore.Release();
        semaphore.Dispose();
    }

    public async Task OnNewJobStarted()
    {
        if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(15)))
        {
            // Cannot flush!
            flushDone = true;
            return;
        }

        try
        {
            await FlushBuffer();
        }
        finally
        {
            semaphore.Release();
            flushDone = true;
        }

        openSectionName = null;
        openSectionNumber = 0;

        flushTask?.Wait();
        flushTask = null;
        hashFlushTask = false;
        lastFlushTime = DateTime.MinValue;
    }

    public async Task OpenNewSection(string sectionName)
    {
        if (cancel)
            throw new ObjectDisposedException("Forwarder closed");

        sectionName = sectionName.Truncate(IJobOutputForwarder.MaxSectionLength);

        // We use different wait times for different operations so that things might be able to resume if we lose the
        // server connection for a few minutes but then manage to regain it
        if (!await semaphore.WaitAsync(TimeSpan.FromMinutes(21)))
        {
            throw new TimeoutException("Timed out waiting for output lock, output flushing is stuck");
        }

        try
        {
            if (openSectionName != null)
            {
                // Close previous section
                buffer.Append("Section closed by the adapter as actual output has not closed this section " +
                    "correctly\n");
                await FlushBuffer();
                await onSectionClosed(openSectionName, openSectionNumber, false);
            }

            openSectionName = sectionName;
            ++openSectionNumber;
            await onSectionOpened(sectionName, openSectionNumber);
            lastFlushTime = DateTime.MinValue;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task CloseSection(bool success)
    {
        if (cancel)
            throw new ObjectDisposedException("Forwarder closed");

        if (!await semaphore.WaitAsync(TimeSpan.FromMinutes(20)))
        {
            throw new TimeoutException("Timed out waiting for output lock, output flushing is stuck");
        }

        try
        {
            if (openSectionName == null)
            {
                buffer.Append($"Error: output tried to close a section where there isn't one with status: {success}");
                return;
            }

            await FlushBuffer();
            await onSectionClosed(openSectionName, openSectionNumber, success);
            openSectionName = null;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task ForwardOutputToActiveSection(string output)
    {
        if (cancel)
            throw new ObjectDisposedException("Forwarder closed");

        if (!await semaphore.WaitAsync(TimeSpan.FromMinutes(15)))
        {
            throw new TimeoutException("Timed out waiting for output lock, output flushing is stuck");
        }

        try
        {
            if (openSectionName == null)
            {
                if (buffer.Length < targetOutputBatchSize * 100)
                {
                    buffer.Append($"Error: output without active section: {output}");
                }

                return;
            }

            buffer.Append(output);

            if (usesImmediateOutput)
            {
                await FlushBuffer();
            }
            else
            {
                // Flushing logic
                if (DateTime.UtcNow - lastFlushTime > outputFlushDelay || output.Length > targetOutputBatchSize * 0.9f)
                {
                    // Flush if time or output is already long
                    await FlushBuffer();
                }
                else
                {
                    // Otherwise, queue a task to flush later when we hopefully either have received more output or
                    // won't likely for a while
                    if (!hashFlushTask)
                    {
                        hashFlushTask = true;
                        flushDone = false;
                        flushTask = Task.Run(WaitAndFlushAsync);
                    }
                    else if (flushDone && flushTask != null)
                    {
                        // Clear out old flush tasks
                        try
                        {
                            await flushTask;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Error flushing output: {e}");
                        }

                        flushTask = null;
                        flushDone = false;
                        hashFlushTask = false;
                    }
                }
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task WaitAndFlushAsync()
    {
        await Task.Delay(outputFlushDelay);

        if (cancel)
            return;

        if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            // Cannot flush, rely on something else flushing
            flushDone = true;
            return;
        }

        try
        {
            if (cancel)
                return;

            await FlushBuffer();
        }
        finally
        {
            semaphore.Release();
            flushDone = true;
        }
    }

    private async Task FlushBuffer()
    {
        if (cancel)
            return;

        if (buffer.Length == 0 || openSectionName == null)
            return;

        // Auto cut buffer if it is too big
        if (buffer.Length > targetOutputBatchSize * 3)
        {
            for (int i = 0; i < buffer.Length; i += targetOutputBatchSize)
            {
                await onSectionOutput(openSectionName, openSectionNumber,
                    buffer.ToString(i, Math.Min(targetOutputBatchSize, buffer.Length - i)));
            }
        }
        else
        {
            await onSectionOutput(openSectionName, openSectionNumber, buffer.ToString());
        }

        buffer.Clear();
        lastFlushTime = DateTime.UtcNow;
    }
}

/// <summary>
///   Gathers output into a section structure from an output forwarder
/// </summary>
public sealed class OutputGatherer : IJobOutputForwarder, IDisposable
{
    private readonly SimpleJobOutputForwarder forwarder;

    public OutputGatherer(int targetOutputBatchSize = 762, float flushDelay = -1)
    {
        forwarder = new SimpleJobOutputForwarder(OnClosed, OnOpened, OnText, targetOutputBatchSize, flushDelay);
    }

    public List<SectionData> Sections { get; } = new();

    public SectionData? OpenSection { get; private set; }

    // Interface implementation
    public bool HasOpenSection => forwarder.HasOpenSection;

    public async Task OnNewJobStarted()
    {
        await forwarder.OnNewJobStarted();

        // Clear after the underlying call to make sure buffers are cleared already
        Sections.Clear();
        OpenSection = null;
    }

    public Task OpenNewSection(string sectionName)
    {
        return forwarder.OpenNewSection(sectionName);
    }

    public Task CloseSection(bool success)
    {
        return forwarder.CloseSection(success);
    }

    public Task ForwardOutputToActiveSection(string output)
    {
        return forwarder.ForwardOutputToActiveSection(output);
    }

    public void Dispose()
    {
        forwarder.Dispose();
    }

    private Task OnText(string name, int sectionId, string text)
    {
        if (OpenSection == null || OpenSection.Id != sectionId)
            throw new InvalidOperationException("No active section to close / wrong section id");

        OpenSection.Text.Append(text);
        return Task.CompletedTask;
    }

    private Task OnOpened(string name, int sectionId)
    {
        if (OpenSection != null)
            throw new InvalidOperationException("Already have an open section");

        OpenSection = new SectionData(name, sectionId);
        Sections.Add(OpenSection);
        return Task.CompletedTask;
    }

    private Task OnClosed(string name, int sectionId, bool success)
    {
        if (OpenSection == null || OpenSection.Id != sectionId)
            throw new InvalidOperationException("No active section to close / wrong section id");

        OpenSection.SetFinished(success);
        OpenSection = null;
        return Task.CompletedTask;
    }

    public class SectionData(string name, int id)
    {
        public readonly string Name = name;
        public readonly int Id = id;
        public readonly StringBuilder Text = new();

        public bool Success;
        public bool Closed;

        public void SetFinished(bool success)
        {
            if (Closed)
                throw new InvalidOperationException("Already closed");

            Closed = true;
            Success = success;
        }
    }
}
