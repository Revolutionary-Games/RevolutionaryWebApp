namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;
using Enums;

public class UserGroupInfo
{
    public GroupType Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;
}
