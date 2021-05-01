namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class LauncherLinkDTO : ClientSideTimedModel
    {
        public string LastIp { get; set; }

        public DateTime? LastConnection { get; set; }

        public int TotalApiCalls { get; set; } = 0;
    }
}
