namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using SharedBase.ModelVerifiers;

public class FeedDiscordWebhookDTO : IIdentifiable
{
    public long FeedId { get; set; }

    [Required]
    [StringLength(300, MinimumLength = 5)]
    [MustContain("http")]
    public string WebhookUrl { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? CustomItemFormat { get; set; }

    [JsonIgnore]
    public long Id
    {
        get => FeedId ^ WebhookUrl.GetHashCode();
        set => throw new NotSupportedException();
    }
}
