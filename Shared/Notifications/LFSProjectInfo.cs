namespace ThriveDevCenter.Shared.Notifications
{
    using System;

    // TODO: rename this to change event
    public class LFSProjectInfo : SerializedNotification
    {
        public string Name { get; set; }

        public bool Public { get; set; }

        public long Size { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
