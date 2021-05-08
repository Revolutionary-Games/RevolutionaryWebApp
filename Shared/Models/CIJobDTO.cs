namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.Text.Json.Serialization;

    public class CIJobDTO : IIdentifiable
    {
        public long CiProjectId { get; set; }
        public long CiBuildId { get; set; }

        public long CiJobId { get; set; }

        public string JobName { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public CIJobState State { get; set; }
        public string ProjectName { get; set; }
        public bool Succeeded { get; set; }

        /// <summary>
        ///   Used for notifications to detect which model was updated, that's why this shouldn't be super bad that
        ///   we generate a fake ID like this
        /// </summary>
        [JsonIgnore]
        public long Id => (CiBuildId << 12) + (CiJobId << 7) + CiProjectId;

        [JsonIgnore]
        public string NotificationsId => CiProjectId + "_" + CiBuildId + "_" + CiJobId;
    }
}
