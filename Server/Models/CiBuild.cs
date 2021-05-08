namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;
    using Utilities;

    public class CiBuild : IUpdateNotifications
    {
        public long CiProjectId { get; set; }

        [AllowSortingBy]
        public long CiBuildId { get; set; }

        /// <summary>
        ///   The hash of the commit triggering this build. The build can also contain other commits
        /// </summary>
        [Required]
        public string CommitHash { get; set; }

        /// <summary>
        ///   Reference to the remote ref we need to checkout to run this build
        /// </summary>
        [Required]
        public string RemoteRef { get; set; }

        /// <summary>
        ///   When this build was started / created
        /// </summary>
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public BuildStatus Status { get; set; } = BuildStatus.Running;

        public CiProject CiProject { get; set; }

        public ICollection<CiJob> CiJobs { get; set; } = new HashSet<CiJob>();

        public CIBuildDTO GetDTO()
        {
            return new()
            {
                CiProjectId = CiProjectId,
                CiBuildId =CiBuildId,
                CommitHash = CommitHash,
                RemoteRef = RemoteRef,
                CreatedAt = CreatedAt,
                Status = Status,
                ProjectName = CiProject?.Name ?? CiProjectId.ToString()
            };
        }

        public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
        {
            yield return new Tuple<SerializedNotification, string>(new CIProjectBuildsListUpdated()
            {
                Type = entityState.ToChangeType(),
                Item = GetDTO()
            }, NotificationGroups.CIProjectBuildsUpdatedPrefix + CiProjectId);

            var notificationsId = CiProjectId + "_" + CiBuildId;

            yield return new Tuple<SerializedNotification, string>(new CIBuildUpdated()
            {
                Item = GetDTO()
            }, NotificationGroups.CIProjectsBuildUpdatedPrefix + notificationsId);
        }
    }
}
