namespace ThriveDevCenter.Shared.Models;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Enums;

/// <summary>
///   Info on which groups a user is in
/// </summary>
public class CachedUserGroups : IUserGroupData
{
    [JsonConstructor]
    public CachedUserGroups(List<GroupType> groups)
    {
        Groups = groups;
    }

    public CachedUserGroups(IEnumerable<GroupType> groups)
    {
        Groups = groups.ToList();
    }

    public CachedUserGroups(params GroupType[] groups)
    {
        Groups = groups;
    }

    [JsonInclude]
    public IEnumerable<GroupType> Groups { get; }
}
