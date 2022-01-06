namespace ThriveDevCenter.Shared.Models
{
    using System;
    using Enums;

    public class CrashReportInfo : ClientSideTimedModel
    {
        public bool Public { get; set; }
        public ReportState State { get; set; }
        public ThrivePlatform Platform { get; set; }
        public DateTime HappenedAt { get; set; }
        public string ExitCodeOrSignal { get; set; }
        public string StoreOrVersion { get; set; }
    }
}
