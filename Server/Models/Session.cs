namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Net;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Utilities;

    [Index(nameof(UserId))]
    [Index(nameof(HashedId), IsUnique = true)]
    public class Session : IContainsHashedLookUps
    {
        [Key]
        [HashedLookUp]
        public Guid Id { get; set; } = Guid.NewGuid();

        public string HashedId { get; set; }

        public long? UserId { get; set; }
        public User User { get; set; }
        // TODO: could move to a model where the Sessions are deleted when user is forced to logout
        public long SessionVersion { get; set; } = 1;

        public string SsoNonce { get; set; }
        public string StartedSsoLogin { get; set; }

        public string SsoReturnUrl { get; set; }

        /// <summary>
        ///   Used to timeout started sso requests
        /// </summary>
        public DateTime? SsoStartTime { get; set; }

        /// <summary>
        ///   Used to clear old sessions
        /// </summary>
        [AllowSortingBy]
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;

        public IPAddress LastUsedFrom { get; set; }
    }
}
