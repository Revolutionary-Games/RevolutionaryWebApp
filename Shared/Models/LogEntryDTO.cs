namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class LogEntryDTO : ClientSideModel
    {
        public string Message { get; set; }
        public DateTime CreatedAt { get; set; }
        public long? TargetUserId { get; set; }
    }
}
