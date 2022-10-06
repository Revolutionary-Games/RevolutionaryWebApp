namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using Enums;

public class CrashReportInfo : ClientSideTimedModel
{
    public bool Public { get; set; }
    public ReportState State { get; set; }
    public ThrivePlatform Platform { get; set; }
    public DateTime HappenedAt { get; set; }

    [Required]
    public string ExitCodeOrSignal { get; set; } = string.Empty;

    public string? StoreOrVersion { get; set; }
}
