namespace RevolutionaryWebApp.Shared.Models;

using System;
using DevCenterCommunication.Models;

public class BackupRunStatusDTO : IIdentifiable
{
    public long Id { get; set; }

    public string Host { get; set; } = string.Empty;

    public BackupOperation Operation { get; set; }

    public DateTime FinishedAt { get; set; }

    public int ExitCode { get; set; }
}
