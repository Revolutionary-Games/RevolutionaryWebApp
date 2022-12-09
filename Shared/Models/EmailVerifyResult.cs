namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
///   Result of client verifying an email address. If this is received, it is always a success.
/// </summary>
public class EmailVerifyResult
{
    /// <summary>
    ///   URL to redirect the client to
    /// </summary>
    [Required]
    public string RedirectTo { get; set; } = string.Empty;
}
