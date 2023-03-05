namespace ThriveDevCenter.Server.Models;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Enums;
using Shared;

/// <summary>
///   A group of users who have certain permissions
/// </summary>
public class UserGroup
{
    public UserGroup(GroupType id, string name)
    {
        Id = id;
        Name = name;
    }

    [Key]
    [AllowSortingBy]
    public GroupType Id { get; set; }

    public string Name { get; set; }

    public ICollection<User> Members { get; set; } = new HashSet<User>();

    public UserGroupExtraData? ExtraData { get; set; }
}
