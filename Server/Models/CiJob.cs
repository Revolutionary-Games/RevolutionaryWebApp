namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;
    using Utilities;

    public class CiJob : IUpdateNotifications
    {
        public long CiProjectId { get; set; }

        public long CiBuildId { get; set; }

        [AllowSortingBy]
        public long CiJobId { get; set; }

        public CIJobState State { get; set; } = CIJobState.Starting;

        public bool Succeeded { get; set; } = false;

        public DateTime? FinishedAt { get; set; }

        [Required]
        [AllowSortingBy]
        public string JobName { get; set; }

        [ForeignKey("CiProjectId,CiBuildId")]
        public CiBuild Build { get; set; }

        public ICollection<CiJobArtifact> CiJobArtifacts { get; set; } = new HashSet<CiJobArtifact>();

        public ICollection<CiJobOutputSection> CiJobOutputSections { get; set; } = new HashSet<CiJobOutputSection>();

        public CIJobDTO GetDTO()
        {
            return new()
            {
                CiProjectId = CiProjectId,
                CiBuildId = CiBuildId,
                CiJobId = CiJobId,
                CreatedAt = Build?.CreatedAt,
                FinishedAt = FinishedAt,
                JobName = JobName,
                State = State,
                Succeeded = Succeeded,
                ProjectName = Build?.CiProject?.Name ?? CiProjectId.ToString()
            };
        }

        public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
        {
            var dto = GetDTO();
            var buildNotificationsId = CiProjectId + "_" + CiBuildId;

            yield return new Tuple<SerializedNotification, string>(new CIProjectBuildJobsListUpdated()
            {
                Type = entityState.ToChangeType(),
                Item = dto
            }, NotificationGroups.CIProjectBuildJobsUpdatedPrefix + buildNotificationsId);

            var notificationsId =  buildNotificationsId + "_" + CiJobId;

            yield return new Tuple<SerializedNotification, string>(new CIJobUpdated()
            {
                Item = dto
            }, NotificationGroups.CIProjectsBuildsJobUpdatedPrefix + notificationsId);
        }
    }
}
