namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Enums;
using Hangfire;
using Jobs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;
using Utilities;

[Index(nameof(Email), IsUnique = true)]
[Index(nameof(HashedApiToken), IsUnique = true)]
[Index(nameof(HashedLfsToken), IsUnique = true)]
[Index(nameof(HashedLauncherLinkCode), IsUnique = true)]
public class User : IdentityUser<long>, ITimestampedModel, IIdentity, IContainsHashedLookUps, IUpdateNotifications
{
    public bool Local { get; set; }

    [AllowSortingBy]
    public string? SsoSource { get; set; }

    [HashedLookUp]
    public string? ApiToken { get; set; }

    public string? HashedApiToken { get; set; }

    [HashedLookUp]
    public string? LfsToken { get; set; }

    public string? HashedLfsToken { get; set; }

    // TODO: remove the nullability here
    [AllowSortingBy]
    public bool? Suspended { get; set; } = false;

    public string? SuspendedReason { get; set; }
    public bool? SuspendedManually { get; set; } = false;

    [HashedLookUp]
    public string? LauncherLinkCode { get; set; }

    public string? HashedLauncherLinkCode { get; set; }
    public DateTime? LauncherCodeExpires { get; set; }

    public int TotalLauncherLinks { get; set; } = 0;

    // Need to reimplement these, as we inherit IdentityUser
    [AllowSortingBy]
    public DateTime CreatedAt { get; set; } = DateTime.Now.ToUniversalTime();

    [AllowSortingBy]
    public DateTime UpdatedAt { get; set; } = DateTime.Now.ToUniversalTime();

    [NotMapped]
    public string AuthenticationType
    {
        get => Local ? "LocalUser" : "Sso" + SsoSource;
        set => throw new NotSupportedException();
    }

    [NotMapped]
    public bool IsAuthenticated { get => true; set => throw new NotSupportedException(); }

    [NotMapped]
    public string Name { get => UserName!; set => UserName = value; }

    /// <summary>
    ///   Builds verified by this user
    /// </summary>
    public ICollection<DevBuild> DevBuilds { get; set; } = new HashSet<DevBuild>();

    /// <summary>
    ///   Launchers linked to this user
    /// </summary>
    public ICollection<LauncherLink> LauncherLinks { get; set; } = new HashSet<LauncherLink>();

    /// <summary>
    ///   Stored files owned by this user
    /// </summary>
    public ICollection<StorageItem> StorageItems { get; set; } = new HashSet<StorageItem>();

    /// <summary>
    ///   Stored file versions uploaded by this user
    /// </summary>
    public ICollection<StorageItemVersion> UploadedStorageItemVersions { get; set; } =
        new HashSet<StorageItemVersion>();

    /// <summary>
    ///   Stored files last modified by this user
    /// </summary>
    public ICollection<StorageItem> LastModifiedStorageItems { get; set; } = new HashSet<StorageItem>();

    /// <summary>
    ///   Deleted files that used to be in a folder owned by this user
    /// </summary>
    public ICollection<StorageItemDeleteInfo> OwnerOfOriginalFolderOfDeleted { get; set; } =
        new HashSet<StorageItemDeleteInfo>();

    /// <summary>
    ///   Automated server actions that have targeted this user
    /// </summary>
    public ICollection<LogEntry> TargetedInLogs { get; set; } = new HashSet<LogEntry>();

    /// <summary>
    ///   Admin actions that have been performed targeting this user
    /// </summary>
    public ICollection<AdminAction> TargetedByAdminActions { get; set; } = new HashSet<AdminAction>();

    /// <summary>
    ///   Admin actions performed by this user
    /// </summary>
    public ICollection<AdminAction> PerformedAdminActions { get; set; } = new HashSet<AdminAction>();

    /// <summary>
    ///   Normal level actions performed by this user
    /// </summary>
    public ICollection<ActionLogEntry> PerformedActions { get; set; } = new HashSet<ActionLogEntry>();

    public ICollection<ExecutedMaintenanceOperation> ExecutedMaintenanceOperations { get; set; } =
        new HashSet<ExecutedMaintenanceOperation>();

    public ICollection<UserGroup> Groups { get; set; } = new HashSet<UserGroup>();

    /// <summary>
    ///   Active sessions of user
    /// </summary>
    public ICollection<Session> Sessions { get; set; } = new HashSet<Session>();

    /// <summary>
    ///   CLA signatures performed by this user
    /// </summary>
    public ICollection<ClaSignature> ClaSignatures { get; set; } = new HashSet<ClaSignature>();

    public ICollection<Meeting> OwnerOfMeetings { get; set; } = new HashSet<Meeting>();

    public ICollection<Meeting> SecretaryOfMeetings { get; set; } = new HashSet<Meeting>();

    public ICollection<Meeting> ChairmanOfMeetings { get; set; } = new HashSet<Meeting>();

    public ICollection<MeetingMember> MemberOfMeetings { get; set; } = new HashSet<MeetingMember>();

    public ICollection<MeetingPollVotingRecord> VotedInPollsRecords { get; set; } =
        new HashSet<MeetingPollVotingRecord>();

    public ICollection<SentBulkEmail> SentBulkEmails { get; set; } = new HashSet<SentBulkEmail>();

    public ICollection<CrashReport> LastEditedCrashReportDescriptions { get; set; } = new HashSet<CrashReport>();

    public ICollection<DebugSymbol> CreatedDebugSymbols { get; set; } = new HashSet<DebugSymbol>();

    public ICollection<MeetingPoll> ManuallyClosedPolls { get; set; } = new HashSet<MeetingPoll>();

    public AssociationMember? AssociationMember { get; set; }

    /// <summary>
    ///   Set to this user's groups once <see cref="ComputeUserGroups"/> has been called
    /// </summary>
    [NotMapped]
    public CachedUserGroups? ResolvedGroups { get; private set; }

    public static void OnNewUserCreated(User user, IBackgroundJobClient jobClient)
    {
        jobClient.Schedule<CheckAssociationStatusForUserJob>(x => x.Execute(user.Email!, CancellationToken.None),
            TimeSpan.FromSeconds(30));
    }

    /// <summary>
    ///   Must be called when the user's groups have changed (and at most 30 seconds before saving the data to the DB).
    ///   If not called the group change will not work!
    /// </summary>
    /// <param name="jobClient">This is used to queue maintenance jobs to keep DB data consistent</param>
    public void OnGroupsChanged(IBackgroundJobClient jobClient)
    {
        jobClient.Schedule<UpdateUserGroupCacheJob>(x => x.Execute(Id, CancellationToken.None),
            TimeSpan.FromSeconds(30));
    }

    /// <summary>
    ///   Loads this user's groups from the database
    /// </summary>
    /// <param name="database">The database to load from</param>
    /// <returns>The loaded group data</returns>
    public async Task<CachedUserGroups> ComputeUserGroups(ApplicationDbContext database)
    {
        if (ResolvedGroups != null)
            return ResolvedGroups;

        var groupIds =
            await database.Database
                .SqlQuery<GroupType>($"SELECT groups_id FROM user_user_group WHERE members_id = {Id}").ToListAsync();

        // Add the "user" group automatically if the user is not restricted
        if (!groupIds.Contains(GroupType.RestrictedUser))
            groupIds.Add(GroupType.User);

        var cache = new CachedUserGroups(groupIds);
        ResolvedGroups = cache;
        return cache;
    }

    public CachedUserGroups AccessCachedGroupsOrThrow()
    {
        if (ResolvedGroups != null)
            return ResolvedGroups;

        throw new InvalidOperationException("This user object does not have groups loaded");
    }

    /// <summary>
    ///   Gets groups for this user but only if the groups navigation property was accessed
    /// </summary>
    public CachedUserGroups ProcessGroupDataFromLoadedGroups()
    {
        if (ResolvedGroups != null)
            return ResolvedGroups;

        var groups = Groups.Select(g => g.Id);

        // As the users group doesn't really exist, inject that manually (similarly to ComputeUserGroups)
        if (Groups.All(g => g.Id != GroupType.RestrictedUser))
            groups = groups.Append(GroupType.User);

        var cache = new CachedUserGroups(groups);

        ResolvedGroups = cache;
        return cache;
    }

    public void SetGroupsFromSessionCache(Session sessionObject)
    {
        ResolvedGroups = sessionObject.CachedUserGroups ??
            throw new ArgumentException("Session doesn't have groups set");

        if (!ResolvedGroups.Groups.Any())
            throw new ArgumentException("Session object's groups list is empty");
    }

    public void SetGroupsFromLauncherLinkCache(CachedUserGroups groups)
    {
        ResolvedGroups = groups;

        if (!ResolvedGroups.Groups.Any())
            throw new ArgumentException("Launcher link's groups list is empty");
    }

    /// <summary>
    ///   A testing method to override user groups, needed to make in-memory DB tests work
    /// </summary>
    /// <param name="forcedGroups">The groups to force</param>
    public void ForceResolveGroupsForTesting(CachedUserGroups forcedGroups)
    {
        ResolvedGroups = forcedGroups;
    }

    public AssociationResourceAccess ComputeAssociationAccessLevel()
    {
        // TODO: somehow ensure that the required model was loaded
        if (AssociationMember == null)
        {
            var groupData = AccessCachedGroupsOrThrow();

            if (groupData.HasGroup(GroupType.Developer))
                return AssociationResourceAccess.Developers;

            // Restricted user created for non-association person. Shouldn't happen but let's handle it this way
            // if it does
            if (groupData.HasAccessLevel(GroupType.RestrictedUser))
                return AssociationResourceAccess.Public;

            return AssociationResourceAccess.Users;
        }

        if (AssociationMember.BoardMember)
            return AssociationResourceAccess.BoardMembers;

        return AssociationResourceAccess.AssociationMembers;
    }

    public UserDTO GetDTO(RecordAccessLevel infoLevel)
    {
        var info = new UserDTO
        {
            Id = Id,
            Name = Name,
        };

        switch (infoLevel)
        {
            case RecordAccessLevel.Public:
                break;
            case RecordAccessLevel.Admin:
                info.Suspended = Suspended ?? false;
                info.SuspendedReason = SuspendedReason;
                info.SuspendedManually = SuspendedManually ?? false;

                // And also add all the private stuff on top
                goto case RecordAccessLevel.Private;
            case RecordAccessLevel.Private:
                info.Email = Email;
                info.TotalLauncherLinks = TotalLauncherLinks;
                info.CreatedAt = CreatedAt;
                info.UpdatedAt = UpdatedAt;
                info.HasApiToken = !string.IsNullOrEmpty(ApiToken);
                info.HasLfsToken = !string.IsNullOrEmpty(LfsToken);
                info.Local = Local;
                info.SsoSource = SsoSource;
                info.Groups = AccessCachedGroupsOrThrow();
                info.AssociationMember = AssociationMember != null;
                info.BoardMember = AssociationMember?.BoardMember ?? false;
                info.HasBeenBoardMember = AssociationMember?.HasBeenBoardMember ?? false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(infoLevel), infoLevel, null);
        }

        return info;
    }

    public UserInfo GetInfo(RecordAccessLevel infoLevel)
    {
        var info = new UserInfo
        {
            Id = Id,
            Name = Name,
        };

        switch (infoLevel)
        {
            case RecordAccessLevel.Public:
                break;
            case RecordAccessLevel.Admin:
                info.Suspended = Suspended ?? false;

                // And also add all the private stuff on top
                goto case RecordAccessLevel.Private;
            case RecordAccessLevel.Private:
                info.Email = Email;
                info.CreatedAt = CreatedAt;
                info.UpdatedAt = UpdatedAt;
                info.Local = Local;
                info.SsoSource = SsoSource;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(infoLevel), infoLevel, null);
        }

        return info;
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new UserListUpdated
        {
            Type = entityState.ToChangeType(),

            // TODO: create a separate UserInfo type to use for the list here
            Item = GetInfo(RecordAccessLevel.Admin),
        }, NotificationGroups.UserListUpdated);

        if (entityState != EntityState.Deleted)
        {
            yield return new Tuple<SerializedNotification, string>(new UserUpdated
            {
                Item = GetDTO(RecordAccessLevel.Admin),
            }, NotificationGroups.UserUpdatedPrefixAdminInfo + Id);
        }

        if (entityState == EntityState.Modified)
        {
            yield return new Tuple<SerializedNotification, string>(new UserUpdated
            {
                // Private is safe here as only admins and the user itself can join this group
                Item = GetDTO(RecordAccessLevel.Private),
            }, NotificationGroups.UserUpdatedPrefix + Id);
        }
    }
}
