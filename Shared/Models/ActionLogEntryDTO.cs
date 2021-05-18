namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class ActionLogEntryDTO : ClientSideModel
    {
        public string Message { get; set; }
        public DateTime CreatedAt { get; set; }
        public long? PerformedById { get; set; }
    }
}
