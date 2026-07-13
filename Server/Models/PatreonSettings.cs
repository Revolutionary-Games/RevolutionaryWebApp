namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Utilities;

[Index(nameof(WebhookId), IsUnique = true)]
public class PatreonSettings : UpdateableModel, IDTOCreator<PatreonSettingsDTO>
{
    [UpdateFromClientRequest]
    public bool Active { get; set; } = false;

    [Required]
    public string CreatorToken { get; set; } = string.Empty;

    public string? CreatorRefreshToken { get; set; }

    [Required]
    [UpdateFromClientRequest]
    public string WebhookId { get; set; } = string.Empty;

    [Required]
    public string WebhookSecret { get; set; } = string.Empty;

    public DateTime? LastWebhook { get; set; }

    public DateTime? LastRefreshed { get; set; }

    [UpdateFromClientRequest]
    public string? CampaignId { get; set; }

    [UpdateFromClientRequest]
    public string? DevbuildsRewardId { get; set; }

    [UpdateFromClientRequest]
    public string? VipRewardId { get; set; }

    public bool IsEntitledToDevBuilds(Patron? patron)
    {
        if (patron == null)
            return false;

        return patron.RewardId == DevbuildsRewardId || patron.RewardId == VipRewardId;
    }

    public bool IsEntitledToVIP(Patron? patron)
    {
        if (patron == null)
            return false;

        return patron.RewardId == VipRewardId;
    }

    public PatreonSettingsDTO GetDTO()
    {
        return new PatreonSettingsDTO
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Active = Active,
            WebhookId = WebhookId,
            LastWebhook = LastWebhook,
            LastRefreshed = LastRefreshed,
            CampaignId = CampaignId,
            DevbuildsRewardId = DevbuildsRewardId,
            VipRewardId = VipRewardId,
        };
    }
}
