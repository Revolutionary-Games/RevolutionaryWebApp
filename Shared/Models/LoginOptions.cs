namespace ThriveDevCenter.Shared.Models;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class LoginOptions
{
    [Required]
    public List<LoginCategory> Categories { get; set; } = new();
}

public class LoginCategory
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public List<LoginOption> Options { get; set; } = new();
}

public class LoginOption
{
    [Required]
    public string ReadableName { get; set; } = string.Empty;

    [Required]
    public string InternalName { get; set; } = string.Empty;

    public bool Active { get; set; } = true;

    public bool Local { get; set; }
}
