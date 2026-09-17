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
    public string? DevbuildsTierId { get; set; }

    [UpdateFromClientRequest]
    public string? VipTierId { get; set; }

    public bool IsEntitledToDevBuilds(Patron? patron)
    {
        if (patron == null)
            return false;

        return IsEntitledToTier(patron, DevbuildsTierId) || IsEntitledToTier(patron, VipTierId);
    }

    public bool IsEntitledToVIP(Patron? patron)
    {
        if (patron == null)
            return false;

        return IsEntitledToTier(patron, VipTierId);
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
            DevbuildsTierId = DevbuildsTierId,
            VipTierId = VipTierId,
        };
    }

    private static bool IsEntitledToTier(Patron patron, string? tierId)
    {
        return !string.IsNullOrEmpty(tierId) &&
            (patron.TierId == tierId || patron.EntitledTierIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Contains(tierId, StringComparer.Ordinal));
    }
}
