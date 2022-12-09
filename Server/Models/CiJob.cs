namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;
using Enums;
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

    public bool Succeeded { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    [Required]
    [AllowSortingBy]
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    ///   The podman image to use to run this job, should be in the form of "thing/image:v1"
    /// </summary>
    public string? Image { get; set; }

    /// <summary>
    ///   Used to allow the build server to connect back to us to communicate build logs and status
    /// </summary>
    [HashedLookUp]
    public Guid? BuildOutputConnectKey { get; set; } = Guid.NewGuid();

    public string? HashedBuildOutputConnectKey { get; set; }

    /// <summary>
    ///   Used to detect which server to release after this job is complete
    /// </summary>
    public long RunningOnServerId { get; set; } = -1;

    /// <summary>
    ///   Defines if the RunningOnServerId is external or internal server ID
    /// </summary>
    public bool? RunningOnServerIsExternal { get; set; }

    /// <summary>
    ///   This contains json serialized for of the cache settings for this build. This is sent to the CI executor
    ///   so that it can handle cache setup before cloning the repo.
    /// </summary>
    public string? CacheSettingsJson { get; set; }

    /// <summary>
    ///   Stores permanently which server this job was ran on
    /// </summary>
    public string? RanOnServer { get; set; }

    /// <summary>
    ///   Measures how long it took for the job to start running on a server after it was created
    /// </summary>
    public TimeSpan? TimeWaitingForServer { get; set; }

    [ForeignKey("CiProjectId,CiBuildId")]
    public CiBuild? Build { get; set; }

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
        var section = new CiJobOutputSection
        {
            CiProjectId = CiProjectId,
            CiBuildId = CiBuildId,
            CiJobId = CiJobId,
            CiJobOutputSectionId = sectionId,
            Name = sectionTitle,
            Status = CIJobSectionStatus.Failed,
            Output = content,
        };

        section.CalculateOutputLength();

        await database.CiJobOutputSections.AddAsync(section);
    }

    /// <summary>
    ///   Converts the Image to the name it should have in the DevCenter's storage
    /// </summary>
    /// <returns>The image name</returns>
    public string GetImageFileName()
    {
        if (string.IsNullOrEmpty(Image))
            return "missing";

        return Image.Replace(":v", "_v") + ".tar.xz";
    }

    public CIJobDTO GetDTO()
    {
        return new()
        {
            CiProjectId = CiProjectId,
            CiBuildId = CiBuildId,
            CiJobId = CiJobId,
            CreatedAt = CreatedAt,
            FinishedAt = FinishedAt,
            JobName = JobName,
            State = State,
            Succeeded = Succeeded,
            RanOnServer = RanOnServer,
            TimeWaitingForServer = TimeWaitingForServer,
            ProjectName = Build?.CiProject?.Name ?? CiProjectId.ToString(),
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        var dto = GetDTO();
        var buildNotificationsId = CiProjectId + "_" + CiBuildId;

        yield return new Tuple<SerializedNotification, string>(new CIProjectBuildJobsListUpdated
        {
            Type = entityState.ToChangeType(),
            Item = dto,
        }, NotificationGroups.CIProjectBuildJobsUpdatedPrefix + buildNotificationsId);

        var notificationsId = buildNotificationsId + "_" + CiJobId;

        yield return new Tuple<SerializedNotification, string>(new CIJobUpdated
        {
            Item = dto,
        }, NotificationGroups.CIProjectsBuildsJobUpdatedPrefix + notificationsId);
    }
}
