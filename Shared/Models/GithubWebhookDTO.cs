namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class GithubWebhookDTO : ClientSideTimedModel
{
    [Required]
    public string Secret { get; set; } = string.Empty;

    public DateTime? LastUsed { get; set; }
}
