namespace RevolutionaryWebApp.Client.Services;

using System;

public class InboxNotificationState
{
    public event EventHandler? OnAllMarkedRead;

    public event EventHandler? OnMarkedRead;

    public void MarkRead()
    {
        OnMarkedRead?.Invoke(this, EventArgs.Empty);
    }

    public void MarkAllRead()
    {
        OnAllMarkedRead?.Invoke(this, EventArgs.Empty);
    }
}
