namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;
using Common.Services;
using Enums;
using Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

/// <summary>
///   A single CI job.
/// </summary>
/// <remarks>
///   <para>
///     This has a unique runner index to ensure each runner can have at most a single job at once
///   </para>
///   <para>
///     Indexed by CreatedAt for efficient job querying and for runners to get oldest jobs first to work on them.
///   </para>
/// </remarks>
[Index(nameof(CreatedAt))]
[Index(nameof(ReservedByRunnerId), IsUnique = true)]
public class CiJob : IUpdateNotifications, IContainsHashedLookUps
{
    public long CiProjectId { get; set; }

    public long CiBuildId { get; set; }

    [AllowSortingBy]
    public long CiJobId { get; set; }

    public CIJobState State { get; set; } = CIJobState.Starting;

    public bool Succeeded { get; set; }

    /// <summary>
    ///   When true the <see cref="CiJobOutputSections"/> have been deleted from the DB
    /// </summary>
    public bool OutputPurged { get; set; }

    [AllowSortingBy]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAt { get; set; }

    [Required]
    [AllowSortingBy]
    [MaxLength(256)]
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    ///   The podman image to use to run this job should be in the form of "thing/image:v1"
    /// </summary>
    [MaxLength(256)]
    public string? Image { get; set; }

    /// <summary>
    ///   Limits which runners are allowed to take this job. Separated by ';'
    /// </summary>
    [MaxLength(256)]
    public string? RequiredRunnerTags { get; set; }

    /// <summary>
    ///   Job can be taken at most by a single runner
    /// </summary>
    public long? ReservedByRunnerId { get; set; }

    public RemoteRunner? ReservedByRunner { get; set; }

    /// <summary>
    ///   This contains JSON serialized for of the cache settings for this build. This is sent to the CI executor
    ///   so that it can handle cache setup before cloning the repo.
    /// </summary>
    [MaxLength(8096)]
    public string? CacheSettingsJson { get; set; }

    /// <summary>
    ///   Stores permanently which server this job was run on
    /// </summary>
    [MaxLength(256)]
    public string? RanOnServer { get; set; }

    /// <summary>
    ///   Measures how long it took for the job to start running on a server after it was created
    /// </summary>
    public TimeSpan? TimeWaitingForServer { get; set; }

    [ForeignKey("CiProjectId,CiBuildId")]
    public CiBuild? Build { get; set; }

    public ICollection<CiJobArtifact> CiJobArtifacts { get; set; } = new HashSet<CiJobArtifact>();

    public ICollection<CiJobOutputSection> CiJobOutputSections { get; set; } = new HashSet<CiJobOutputSection>();

    /// <summary>
    ///   xmin-based concurrent edit protection
    /// </summary>
    [Timestamp]
    public uint Version { get; set; }

    /// <summary>
    ///   Used to detect if the output connection is still valid.
    /// </summary>
    public int OutputConnection { get; set; } = -1;

    public static Task NotifyNewJobs(IHubContext<RunnerNotificationsHub, IRunnerNotifications> notifications)
    {
        return notifications.Clients.All.ReceiveNewJobNotice();
    }

    /// <summary>
    ///   Correctly deletes a set of CI jobs
    /// </summary>
    /// <param name="database">The database the jobs are from</param>
    /// <param name="jobs">The jobs to delete</param>
    public static void DeleteJobs(ApplicationDbContext database, ICollection<CiJob> jobs)
    {
        // TODO: implement deleting job artifacts if those exist

        // Delete the jobs from the database
        database.CiJobs.RemoveRange(jobs);
    }

    public void SetFinishSuccess(bool success)
    {
        Succeeded = success;
        State = CIJobState.Finished;

        // In case the runner connection already managed to set this
        FinishedAt ??= DateTime.UtcNow;

        ReservedByRunnerId = null;
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
        return new CIJobDTO
        {
            CiProjectId = CiProjectId,
            CiBuildId = CiBuildId,
            CiJobId = CiJobId,
            CreatedAt = CreatedAt,
            FinishedAt = FinishedAt,
            JobName = JobName,
            State = State,
            Succeeded = Succeeded,
            OutputPurged = OutputPurged,
            RanOnServer = RanOnServer,
            TimeWaitingForServer = TimeWaitingForServer,
            ProjectName = Build?.CiProject?.Name ?? CiProjectId.ToString(),
            RequiredRunnerTags = RequiredRunnerTags,
            Image = Image,
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
