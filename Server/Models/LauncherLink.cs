using System;

namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;
    using Utilities;

    [Index(nameof(HashedLinkCode), IsUnique = true)]
    [Index(nameof(UserId))]
    public class LauncherLink : UpdateableModel, IContainsHashedLookUps
    {
        [Required]
        [HashedLookUp]
        public string LinkCode { get; set; }

        public string HashedLinkCode { get; set; }

        [Required]
        public string LastIp { get; set; }

        public DateTime? LastConnection { get; set; }

        public int TotalApiCalls { get; set; } = 0;

        public long UserId { get; set; }
        public User User { get; set; }
    }
}
