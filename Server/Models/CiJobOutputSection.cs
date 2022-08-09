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

    public class CiJobOutputSection : IUpdateNotifications
    {
        public long CiProjectId { get; set; }

        public long CiBuildId { get; set; }

        public long CiJobId { get; set; }

        [AllowSortingBy]
        public long CiJobOutputSectionId { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public CIJobSectionStatus Status { get; set; } = CIJobSectionStatus.Running;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? FinishedAt { get; set; }

        [Required]
        public string Output { get; set; } = string.Empty;

        /// <summary>
        ///   The length of the Output, stored separately for more convenient checks regarding the length
        /// </summary>
        public long OutputLength { get; set; }

        [ForeignKey("CiProjectId,CiBuildId,CiJobId")]
        public CiJob? Job { get; set; }

        /// <summary>
        ///   Fills the OutputLength property based on the Output property
        /// </summary>
        public void CalculateOutputLength()
        {
            OutputLength = Output.Length;
        }

        public CIJobOutputSectionInfo GetInfo()
        {
            return new()
            {
                CiProjectId = CiProjectId,
                CiBuildId = CiBuildId,
                CiJobId = CiJobId,
                CiJobOutputSectionId = CiJobOutputSectionId,
                Name = Name,
                Status = Status,
                OutputLength = OutputLength,
                StartedAt = StartedAt,
                FinishedAt = FinishedAt,
            };
        }

        public CIJobOutputSectionDTO GetDTO()
        {
            return new()
            {
                CiProjectId = CiProjectId,
                CiBuildId = CiBuildId,
                CiJobId = CiJobId,
                CiJobOutputSectionId = CiJobOutputSectionId,
                Name = Name,
                Status = Status,
                Output = Output,
                OutputLength = OutputLength,
                StartedAt = StartedAt,
                FinishedAt = FinishedAt,
            };
        }

        public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
        {
            var info = GetInfo();
            var jobNotificationsId = CiProjectId + "_" + CiBuildId + "_" + CiJobId;

            yield return new Tuple<SerializedNotification, string>(new CIProjectBuildJobOutputSectionsListUpdated()
            {
                Type = entityState.ToChangeType(),
                Item = info,
            }, NotificationGroups.CIProjectBuildJobSectionsUpdatedPrefix + jobNotificationsId);

            // var notificationsId = jobNotificationsId + "_" + CiJobOutputSectionId;

            // It would be very wasteful to send the full output after each added couple of lines of output, so
            // the section updated also sends just the info
            // yield return new Tuple<SerializedNotification, string>(new CIJobOutputSectionUpdated()
            // {
            //     Item = info
            // }, NotificationGroups.CIProjectsBuildsJobsSectionUpdatedPrefix + notificationsId);
        }
    }
}
