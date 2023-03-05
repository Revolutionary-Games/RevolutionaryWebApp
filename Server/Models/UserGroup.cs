namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Enums;
using Shared;

/// <summary>
///   A group of users who have certain permissions
/// </summary>
public class UserGroup
{
    public UserGroup(GroupType id, string name, DateTime createdAt)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
    }

    [Key]
    [AllowSortingBy]
    public GroupType Id { get; set; }

    public string Name { get; set; }

    [AllowSortingBy]
    public DateTime CreatedAt { get; set; }

    public ICollection<User> Members { get; set; } = new HashSet<User>();
}
