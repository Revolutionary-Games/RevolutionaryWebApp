namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;

public class SigningStartResponse
{
    public bool SessionStarted { get; set; }

    /// <summary>
    ///   Tells where the client should go next
    /// </summary>
    [Required]
    public string NextPath { get; set; } = string.Empty;
}