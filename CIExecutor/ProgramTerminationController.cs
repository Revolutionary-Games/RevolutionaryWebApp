using System;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
///   Handles CTRL+C from the user
/// </summary>
public sealed class ProgramTerminationController : IDisposable
{
    private readonly Action firstCtrlCCallback;
    private readonly CancellationTokenSource cts = new();
    private readonly PosixSignalRegistration? sigtermRegistration;
    private readonly Lock sync = new();

    private int ctrlCCount;
    private bool disposed;

    public ProgramTerminationController(Action firstCtrlCCallback)
    {
        this.firstCtrlCCallback = firstCtrlCCallback;

        Console.CancelKeyPress += OnCancelKeyPress;

        if (!OperatingSystem.IsWindows())
        {
            sigtermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSigTerm);
        }
        else
        {
            Console.WriteLine("SIGTERM not supported on Windows");
        }
    }

    public CancellationToken Token => cts.Token;

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        Console.CancelKeyPress -= OnCancelKeyPress;

        sigtermRegistration?.Dispose();

        cts.Dispose();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        lock (sync)
        {
            ++ctrlCCount;

            if (ctrlCCount == 1)
            {
                // First CTRL+C: run custom shutdown callback, but do not cancel yet.
                Console.WriteLine("CTRL+C pressed, shutting down after next job...");
                e.Cancel = true;
                firstCtrlCCallback();
            }
            else if (ctrlCCount < 3)
            {
                // Second CTRL+C: cancel immediately.
                Console.WriteLine("CTRL+C pressed, shutting down ASAP");
                cts.Cancel();
                e.Cancel = true;
            }

            // And on further presses don't suppress it
        }
    }

    private void OnSigTerm(PosixSignalContext context)
    {
        lock (sync)
        {
            cts.Cancel();
            context.Cancel = true;
        }

        Console.WriteLine("Received SIGTERM, will try to quit ASAP...");
    }
}
