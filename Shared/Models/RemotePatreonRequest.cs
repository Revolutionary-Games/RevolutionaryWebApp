namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;

public class RemotePatreonRequest
{
    public long Id { get; set; }

    public string? Token { get; set; }
}

public class PatreonRewardsRequest : RemotePatreonRequest
{
    [Required]
    public string CampaignId { get; set; } = string.Empty;
}
