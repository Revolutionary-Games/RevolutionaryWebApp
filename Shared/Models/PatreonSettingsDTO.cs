namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class PatreonSettingsDTO : ClientSideTimedModel
{
    public bool Active { get; set; }

    /// <summary>
    ///   Write-only, can't be retrieved from the server
    /// </summary>
    public string? CreatorToken { get; set; }

    [Required]
    public string WebhookId { get; set; } = string.Empty;

    /// <summary>
    ///   Not fetchable after
    /// </summary>
    public string? WebhookSecret { get; set; }

    public DateTime? LastWebhook { get; set; }

    public DateTime? LastRefreshed { get; set; }

    public string? CampaignId { get; set; }
    public string? DevbuildsRewardId { get; set; }
    public string? VipRewardId { get; set; }
}
