namespace RevolutionaryWebApp.Server.Models;

using System.ComponentModel.DataAnnotations;
using Enums;
using SharedBase.ModelVerifiers;

public class EmailTokenData
{
    [Required]
    [Email]
    public string SentToEmail { get; set; } = string.Empty;

    [Required]
    public EmailVerificationType Type { get; set; }

    [Required]
    [StringLength(1024, MinimumLength = 3)]
    public string VerifiedResourceId { get; set; } = string.Empty;
}
