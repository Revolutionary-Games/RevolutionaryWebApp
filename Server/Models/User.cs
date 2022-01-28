namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Security.Principal;
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

        // TODO: combine these to a single enum field (replace these 2 properties with UserAccessLevel)
        [AllowSortingBy]
        public bool? Developer { get; set; } = false;

        [AllowSortingBy]
        public bool? Admin { get; set; } = false;

        public bool AssociationMember { get; set; }
        public bool BoardMember { get; set; }
        public bool HasBeenBoardMember { get; set; }

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

        public ICollection<MeetingMember> MemberOfMeetings { get; set; } = new HashSet<MeetingMember>();

        public ICollection<MeetingPollVotingRecord> VotedInPollsRecords { get; set; } =
            new HashSet<MeetingPollVotingRecord>();

        public ICollection<SentBulkEmail> SentBulkEmails { get; set; } = new HashSet<SentBulkEmail>();

        public ICollection<CrashReport> LastEditedCrashReportDescriptions { get; set; } = new HashSet<CrashReport>();

        public ICollection<DebugSymbol> CreatedDebugSymbols { get; set; } = new HashSet<DebugSymbol>();

        public bool HasAccessLevel(UserAccessLevel level)
        {
            return ComputeAccessLevel().HasAccess(level);
        }

        public UserAccessLevel ComputeAccessLevel()
        {
            if (Admin == true)
                return UserAccessLevel.Admin;
            if (Developer == true)
                return UserAccessLevel.Developer;

            // Suspended user has no access
            if (Suspended != true)
                return UserAccessLevel.User;

            return UserAccessLevel.NotLoggedIn;
        }

        public AssociationResourceAccess ComputeAssociationAccessLevel()
        {
            if (!AssociationMember)
            {
                if (Developer == true)
                    return AssociationResourceAccess.Developers;

                return AssociationResourceAccess.Users;
            }

            if (BoardMember)
                return AssociationResourceAccess.BoardMembers;

            return AssociationResourceAccess.AssociationMembers;
        }

        public UserInfo GetInfo(RecordAccessLevel infoLevel)
        {
            var info = new UserInfo()
            {
                Id = Id,
                Name = UserName,
                Developer = Developer ?? false
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
                    info.AssociationMember = AssociationMember;
                    info.BoardMember = BoardMember;
                    info.HasBeenBoardMember = HasBeenBoardMember;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(infoLevel), infoLevel, null);
            }

            return info;
        }

        public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
        {
            yield return new Tuple<SerializedNotification, string>(new UserListUpdated()
            {
                Type = entityState.ToChangeType(),

                // TODO: create a separate UserInfo type to use for the list here
                Item = GetInfo(RecordAccessLevel.Admin)
            }, NotificationGroups.UserListUpdated);

            if (entityState != EntityState.Deleted)
            {
                yield return new Tuple<SerializedNotification, string>(new UserUpdated()
                {
                    Item = GetInfo(RecordAccessLevel.Admin)
                }, NotificationGroups.UserUpdatedPrefixAdminInfo + Id);
            }

            if (entityState == EntityState.Modified)
            {
                yield return new Tuple<SerializedNotification, string>(new UserUpdated()
                {
                    // Private is safe here as only admins and the user itself can join this group
                    Item = GetInfo(RecordAccessLevel.Private)
                }, NotificationGroups.UserUpdatedPrefix + Id);
            }
        }
    }
}
