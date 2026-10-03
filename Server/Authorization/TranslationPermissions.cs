namespace RevolutionaryWebApp.Server.Authorization;

using Shared.Models;
using Shared.Models.Enums;

/// <summary>
///   Central authorization rules for translation and dictionary mutations.
/// </summary>
public static class TranslationPermissions
{
    public static bool CanTranslate(IUserGroupData groups, bool isSourceLanguage)
    {
        if (!groups.HasAccessLevel(GroupType.User))
            return false;

        return !isSourceLanguage || groups.HasGroup(GroupType.SourceTextTranslator) || IsAdmin(groups);
    }

    public static bool CanVerify(IUserGroupData groups)
    {
        return groups.HasAccessLevel(GroupType.User) &&
            (groups.HasGroup(GroupType.TranslationVerifier) || IsAdmin(groups));
    }

    public static bool CanEditSourceDictionary(IUserGroupData groups)
    {
        return groups.HasAccessLevel(GroupType.User) &&
            (groups.HasGroup(GroupType.DictionaryManager) || IsAdmin(groups));
    }

    public static bool CanEditTargetDictionary(IUserGroupData groups)
    {
        return groups.HasAccessLevel(GroupType.User);
    }

    private static bool IsAdmin(IUserGroupData groups)
    {
        return groups.HasAccessLevel(GroupType.Admin);
    }
}
