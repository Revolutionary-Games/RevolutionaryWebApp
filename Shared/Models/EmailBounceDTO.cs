namespace RevolutionaryWebApp.Shared.Models;

using System;
using DevCenterCommunication.Models;

public class EmailBounceDTO : ClientSideModel
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;

    public int OutstandingBounces { get; set; }

    public DateTime FirstBounceUtc { get; set; }
    public DateTime LastBounceUtc { get; set; }

    public bool DisabledBySystem { get; set; }

    public int BackoffWeeks { get; set; }
}
