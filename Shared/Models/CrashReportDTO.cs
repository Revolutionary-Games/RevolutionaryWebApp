namespace ThriveDevCenter.Shared.Models
{
    using System;
    using Enums;

    public class CrashReportDTO : ClientSideTimedModel
    {
        public bool Public { get; set; }
        public ReportState State { get; set; }
        public ThrivePlatform Platform { get; set; }
        public DateTime HappenedAt { get; set; }
        public string ExitCodeOrSignal { get; set; }
        public string Store { get; set; }
        public string Version { get; set; }
        public string PrimaryCallstack { get; set; }
        public string Description { get; set; }
        public DateTime? DescriptionLastEdited { get; set; }
        public long? DescriptionLastEditedById { get; set; }
        public long? DuplicateOfId { get; set; }
        public bool CanReProcess { get; set; }
    }
}
