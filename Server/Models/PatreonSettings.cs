using System;
using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;

    [Index(nameof(WebhookId), IsUnique = true)]
    public class PatreonSettings : UpdateableModel
    {
        public bool Active { get; set; } = false;

        [Required]
        public string CreatorToken { get; set; }

        public string CreatorRefreshToken { get; set; }

        [Required]
        public string WebhookId { get; set; }

        [Required]
        public string WebhookSecret { get; set; }

        public DateTime? LastWebhook { get; set; }

        public DateTime? LastRefreshed { get; set; }

        public string CampaignId { get; set; }
        public string DevbuildsRewardId { get; set; }
        public string VipRewardId { get; set; }

        public bool IsEntitledToDevBuilds(Patron patron)
        {
            if (patron == null)
                return false;

            return patron.RewardId == DevbuildsRewardId || patron.RewardId == VipRewardId;
        }

        public bool IsEntitledToVIP(Patron patron)
        {
            if (patron == null)
                return false;

            return patron.RewardId == VipRewardId;
        }
    }
}
