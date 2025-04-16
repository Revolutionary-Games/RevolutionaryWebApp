namespace RevolutionaryWebApp.Shared.Forms;

using System;
using System.ComponentModel.DataAnnotations;

public class UserSuspendRequest
{
    [Required]
    [StringLength(200, MinimumLength = 2)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public DateTime SuspendedUntil { get; set; }
}
