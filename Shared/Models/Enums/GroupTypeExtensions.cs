namespace RevolutionaryWebApp.Shared.Models.Enums;

using System;
using System.Collections.Generic;

public static class GroupTypeExtensions
{
    /// <summary>
    ///   Gets names of valid group types for picking in the GUI (i.e. the enum values but filtered)
    /// </summary>
    /// <returns>Valid group names</returns>
    public static IEnumerable<string> GetValidGroupTypes()
    {
        foreach (var name in Enum.GetNames<GroupType>())
        {
            if (name is "Custom" or "Max")
            {
                continue;
            }

            yield return name;
        }
    }
}
