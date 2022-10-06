namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Principal;
using System.Threading;
using DevCenterCommunication.Models;
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

    // TODO: combine these to a single enum field (replace these 3 properties with UserAccessLevel)
    [AllowSortingBy]
    public bool? Developer { get; set; } = false;

    [AllowSortingBy]
    public bool? Admin { get; set; } = false;

    [AllowSortingBy]
    public bool Restricted { get; set; }

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

    public int SessionVersion { get; set; } = 1;

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
    public string? Name { get => UserName; set => UserName = value; }

    [NotMapped]
    public string NameOrEmail => Name ?? Email;

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

    public static void OnNewUserCreated(User user, IBackgroundJobClient jobClient)
    {
        jobClient.Schedule<CheckAssociationStatusForUserJob>(x => x.Execute(user.Email, CancellationToken.None),
            TimeSpan.FromSeconds(30));
    }

    public bool HasAccessLevel(UserAccessLevel level)
    {
        return ComputeAccessLevel().HasAccess(level);
    }

    public UserAccessLevel ComputeAccessLevel()
    {
        // Suspended user has no access
        if (Suspended == true)
            return UserAccessLevel.NotLoggedIn;

        if (Admin == true)
            return UserAccessLevel.Admin;
        if (Developer == true)
            return UserAccessLevel.Developer;
        if (Restricted)
            return UserAccessLevel.RestrictedUser;

        return UserAccessLevel.User;
    }

    public AssociationResourceAccess ComputeAssociationAccessLevel()
    {
        // TODO: somehow ensure that the required model was loaded
        if (AssociationMember == null)
        {
            if (Developer == true)
                return AssociationResourceAccess.Developers;

            // Restricted user created for non-association person. Shouldn't happen but let's handle it this way
            // if it does
            if (Restricted)
                return AssociationResourceAccess.Public;

            return AssociationResourceAccess.Users;
        }

        if (AssociationMember.BoardMember)
            return AssociationResourceAccess.BoardMembers;

        return AssociationResourceAccess.AssociationMembers;
    }

    public UserInfo GetInfo(RecordAccessLevel infoLevel)
    {
        var info = new UserInfo
        {
            Id = Id,
            Name = UserName,
            Developer = Developer ?? false,
        };

        switch (infoLevel)
        {
            case RecordAccessLevel.Public:
                break;
            case RecordAccessLevel.Admin:
                info.Suspended = Suspended ?? false;
                info.SuspendedReason = SuspendedReason;
                info.SuspendedManually = SuspendedManually ?? false;
                info.Restricted = Restricted;

                // And also add all the private stuff on top
                goto case RecordAccessLevel.Private;
            case RecordAccessLevel.Private:
                info.Email = Email;
                info.Admin = Admin ?? false;
                info.TotalLauncherLinks = TotalLauncherLinks;
                info.CreatedAt = CreatedAt;
                info.UpdatedAt = UpdatedAt;
                info.HasApiToken = !string.IsNullOrEmpty(ApiToken);
                info.HasLfsToken = !string.IsNullOrEmpty(LfsToken);
                info.Local = Local;
                info.SsoSource = SsoSource;
                info.AccessLevel = ComputeAccessLevel();
                info.SessionVersion = SessionVersion;
                info.AssociationMember = AssociationMember != null;
                info.BoardMember = AssociationMember?.BoardMember ?? false;
                info.HasBeenBoardMember = AssociationMember?.HasBeenBoardMember ?? false;
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
                Item = GetInfo(RecordAccessLevel.Admin),
            }, NotificationGroups.UserUpdatedPrefixAdminInfo + Id);
        }

        if (entityState == EntityState.Modified)
        {
            yield return new Tuple<SerializedNotification, string>(new UserUpdated
            {
                // Private is safe here as only admins and the user itself can join this group
                Item = GetInfo(RecordAccessLevel.Private),
            }, NotificationGroups.UserUpdatedPrefix + Id);
        }
    }
}
