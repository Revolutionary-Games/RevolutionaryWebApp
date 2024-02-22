namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Enums;
using Microsoft.EntityFrameworkCore;
using Utilities;

[Index(nameof(HashedId), IsUnique = true)]
public class RedeemableCode : IContainsHashedLookUps
{
    [Key]
    [HashedLookUp]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string HashedId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///   Specifies what this code grants. See CodeRedeemController
    /// </summary>
    [Required]
    public string GrantedResource { get; set; } = string.Empty;

    public bool MultiUse { get; set; } = false;

    /// <summary>
    ///   Number of uses for multi use codes
    /// </summary>
    public int Uses { get; set; }
}
