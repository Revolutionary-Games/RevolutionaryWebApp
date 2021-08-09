namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Net;
    using System.Text.Json.Serialization;
    using Shared.Converters;
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

        // TODO: should move to a model where the Sessions are deleted when user is forced to logout
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

        /// <summary>
        ///   Used also to clear old sessions to enforce total session duration TODO: implement that job
        /// </summary>
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress LastUsedFrom { get; set; }

        public InProgressClaSignature InProgressClaSignature { get; set; }

        public bool IsCloseToExpiry()
        {
            return DateTime.UtcNow - LastUsed > TimeSpan.FromSeconds(AppInfo.SessionExpirySeconds - 3600 * 8);
        }
    }
}
