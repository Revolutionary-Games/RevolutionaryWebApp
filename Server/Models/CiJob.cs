namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;
    using Utilities;

    [Index(nameof(HashedBuildOutputConnectKey), IsUnique = true)]
    public class CiJob : IUpdateNotifications, IContainsHashedLookUps
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

        /// <summary>
        ///   The podman image to use to run this job, should be in the form of "thing/image:v1"
        /// </summary>
        public string Image { get; set; }

        /// <summary>
        ///   Used to allow the build server to connect back to us to communicate build logs and status
        /// </summary>
        [HashedLookUp]
        public Guid? BuildOutputConnectKey { get; set; } = Guid.NewGuid();

        public string HashedBuildOutputConnectKey { get; set; }

        /// <summary>
        ///   Used to detect which server to release after this job is complete
        /// </summary>
        public long RunningOnServerId { get; set; } = -1;

        [ForeignKey("CiProjectId,CiBuildId")]
        public CiBuild Build { get; set; }

        public ICollection<CiJobArtifact> CiJobArtifacts { get; set; } = new HashSet<CiJobArtifact>();

        public ICollection<CiJobOutputSection> CiJobOutputSections { get; set; } = new HashSet<CiJobOutputSection>();

        public void SetFinishSuccess(bool success)
        {
            Succeeded = success;
            State = CIJobState.Finished;
            FinishedAt = DateTime.UtcNow;
            BuildOutputConnectKey = null;
        }

        public async Task CreateFailureSection(ApplicationDbContext database, string content,
            string sectionTitle = "Invalid configuration", long sectionId = 1)
        {
            var section = new CiJobOutputSection()
            {
                CiProjectId = CiProjectId,
                CiBuildId = CiBuildId,
                CiJobId = CiJobId,
                CiJobOutputSectionId = sectionId,
                Name = sectionTitle,
                Status = CIJobSectionStatus.Failed,
                Output = content
            };

            section.CalculateOutputLength();

            await database.CiJobOutputSections.AddAsync(section);
        }

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

            var notificationsId = buildNotificationsId + "_" + CiJobId;

            yield return new Tuple<SerializedNotification, string>(new CIJobUpdated()
            {
                Item = dto
            }, NotificationGroups.CIProjectsBuildsJobUpdatedPrefix + notificationsId);
        }
    }
}
