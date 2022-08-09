namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

public class GithubWebhookDTO : ClientSideTimedModel
{
    [Required]
    public string Secret { get; set; } = string.Empty;

    public DateTime? LastUsed { get; set; }
}