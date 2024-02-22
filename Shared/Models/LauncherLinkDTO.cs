namespace RevolutionaryWebApp.Shared.Models;

using System;
using DevCenterCommunication.Models;

public class LauncherLinkDTO : ClientSideTimedModel
{
    public string? LastIp { get; set; }

    public DateTime? LastConnection { get; set; }

    public int TotalApiCalls { get; set; } = 0;
}
