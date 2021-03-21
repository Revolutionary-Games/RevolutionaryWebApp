namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Security.Principal;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;
    using Shared.Models;

    [Index(nameof(Email), IsUnique = true)]
    [Index(nameof(ApiToken), IsUnique = true)]
    [Index(nameof(LfsToken), IsUnique = true)]
    [Index(nameof(LauncherLinkCode), IsUnique = true)]
    public class User : IdentityUser<long>, ITimestampedModel, IIdentity
    {
        public bool Local { get; set; }
        public string SsoSource { get; set; }

        // TODO: combine these to a single enum field
        public bool? Developer { get; set; } = false;
        public bool? Admin { get; set; } = false;

        public string ApiToken { get; set; }
        public string LfsToken { get; set; }

        public bool? Suspended { get; set; } = false;
        public string SuspendedReason { get; set; }
        public bool? SuspendedManually { get; set; } = false;

        public string LauncherLinkCode { get; set; }
        public DateTime? LauncherCodeExpires { get; set; }

        public int TotalLauncherLinks { get; set; } = 0;

        public int SessionVersion { get; set; } = 1;

        // Need to reimplement these, as we inherit IdentityUser
        public DateTime CreatedAt { get; set; } = DateTime.Now.ToUniversalTime();
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
        public string Name { get => UserName; set => UserName = value; }

        /// <summary>
        ///   Builds verified by this user
        /// </summary>
        public virtual ICollection<DevBuild> DevBuilds { get; set; } = new HashSet<DevBuild>();

        /// <summary>
        ///   Launchers linked to this user
        /// </summary>
        public virtual ICollection<LauncherLink> LauncherLinks { get; set; } = new HashSet<LauncherLink>();

        /// <summary>
        ///   Stored files owned by this user
        /// </summary>
        public virtual ICollection<StorageItem> StorageItems { get; set; } = new HashSet<StorageItem>();

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
                    // TODO: add the suspension reasons etc.
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
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(infoLevel), infoLevel, null);
            }

            return info;
        }
    }
}
