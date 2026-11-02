namespace RevolutionaryWebApp.Client.Services;

using System;

public class InboxNotificationState
{
    public event EventHandler? OnAllMarkedRead;

    public void MarkAllRead()
    {
        OnAllMarkedRead?.Invoke(this, EventArgs.Empty);
    }
}
