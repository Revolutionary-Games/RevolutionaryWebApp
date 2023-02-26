namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using Enums;

public class CrashReportDTO : ClientSideTimedModel, ICloneable
{
    public bool Public { get; set; }
    public ReportState State { get; set; }
    public ThrivePlatform Platform { get; set; }
    public DateTime HappenedAt { get; set; }

    [Required]
    public string ExitCodeOrSignal { get; set; } = string.Empty;

    public string? Store { get; set; }
    public string? Version { get; set; }
    public string? PrimaryCallstack { get; set; }
    public string? CondensedCallstack { get; set; }
    public string? Description { get; set; }
    public DateTime? DescriptionLastEdited { get; set; }
    public long? DescriptionLastEditedById { get; set; }
    public long? DuplicateOfId { get; set; }
    public bool CanReProcess { get; set; }

    public string? AnonymizedReporterIp { get; set; }

    public object Clone()
    {
        return new CrashReportDTO
        {
            Public = Public,
            State = State,
            Platform = Platform,
            HappenedAt = HappenedAt,
            ExitCodeOrSignal = ExitCodeOrSignal,
            Store = Store,
            Version = Version,
            PrimaryCallstack = PrimaryCallstack,
            CondensedCallstack = CondensedCallstack,
            Description = Description,
            DescriptionLastEdited = DescriptionLastEdited,
            DescriptionLastEditedById = DescriptionLastEditedById,
            DuplicateOfId = DuplicateOfId,
            CanReProcess = CanReProcess,
            AnonymizedReporterIp = AnonymizedReporterIp,
        };
    }
}
