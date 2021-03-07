using System;
using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;

    [Index(nameof(Email), IsUnique = true)]
    public class Patron : UpdateableModel
    {
        [Required]
        public string Email { get; set; }

        public string EmailAlias { get; set; }

        [Required]
        public string Username { get; set; }

        public int PledgeAmountCents { get; set; }

        [Required]
        public string RewardId { get; set; }

        public bool? Marked { get; set; } = true;

        public string PatreonToken { get; set; }
        public string PatreonRefreshToken { get; set; }

        public bool? HasForumAccount { get; set; } = false;

        public bool? Suspended { get; set; } = false;
        public string SuspendedReason { get; set; }
    }
}
