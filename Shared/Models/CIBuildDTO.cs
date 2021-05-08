namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.Text.Json.Serialization;

    public class CIBuildDTO : IIdentifiable
    {
        public long CiProjectId { get; set; }
        public long CiBuildId { get; set; }
        public string CommitHash { get; set; }
        public string RemoteRef { get; set; }
        public DateTime CreatedAt { get; set; }
        public BuildStatus Status { get; set; }
        public string ProjectName { get; set; }

        /// <summary>
        ///   Used for notifications to detect which model was updated, that's why this shouldn't be super bad that
        ///   we generate a fake ID like this
        /// </summary>
        [JsonIgnore]
        public long Id => (CiBuildId << 12) + CiProjectId;

        [JsonIgnore]
        public string NotificationsId => CiProjectId + "_" + CiBuildId;
    }
}
