namespace RevolutionaryWebApp.Server.Tests.Authorization.Tests;

using Server.Authorization;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class TranslationPermissionsTests
{
    [Fact]
    public void OrdinaryUserCanTranslateTargetsButNotEnglishOrVerify()
    {
        var groups = new CachedUserGroups(GroupType.User);

        Assert.True(TranslationPermissions.CanTranslate(groups, false));
        Assert.False(TranslationPermissions.CanTranslate(groups, true));
        Assert.False(TranslationPermissions.CanVerify(groups));
        Assert.True(TranslationPermissions.CanEditTargetDictionary(groups));
        Assert.False(TranslationPermissions.CanEditSourceDictionary(groups));
    }

    [Fact]
    public void RestrictedAccountCannotEditEvenWithSpecialistMembership()
    {
        var groups = new CachedUserGroups(GroupType.RestrictedUser, GroupType.TranslationVerifier,
            GroupType.SourceTextTranslator, GroupType.DictionaryManager);

        Assert.False(TranslationPermissions.CanTranslate(groups, false));
        Assert.False(TranslationPermissions.CanTranslate(groups, true));
        Assert.False(TranslationPermissions.CanVerify(groups));
        Assert.False(TranslationPermissions.CanEditSourceDictionary(groups));
    }

    [Fact]
    public void SpecialistAndAdminPermissionsAreIndependentOfAuthor()
    {
        var verifier = new CachedUserGroups(GroupType.User, GroupType.TranslationVerifier);
        var sourceEditor = new CachedUserGroups(GroupType.User, GroupType.SourceTextTranslator);
        var manager = new CachedUserGroups(GroupType.User, GroupType.DictionaryManager);
        var admin = new CachedUserGroups(GroupType.Admin);

        Assert.True(TranslationPermissions.CanVerify(verifier));
        Assert.True(TranslationPermissions.CanTranslate(sourceEditor, true));
        Assert.True(TranslationPermissions.CanEditSourceDictionary(manager));
        Assert.True(TranslationPermissions.CanVerify(admin));
        Assert.True(TranslationPermissions.CanTranslate(admin, true));
        Assert.True(TranslationPermissions.CanEditSourceDictionary(admin));
    }
}
