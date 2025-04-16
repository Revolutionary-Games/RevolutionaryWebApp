namespace RevolutionaryWebApp.Shared.Models.Enums;

/// <summary>
///   Group IDs, contains inbuilt IDs and leaves a bunch of space for custom user-defined ones.
///   Do not change the associated numbers with any values after they are added
/// </summary>
public enum GroupType
{
    // Default inbuilt group types

    /// <summary>
    ///   This represents not-logged in users, so this is not allowed to exist
    /// </summary>
    NotLoggedIn = 0,
    RestrictedUser = 1,

    /// <summary>
    ///   This is assumed for all users that are not in the restricted group, so an entity for this group does not exist
    /// </summary>
    User = 2,

    Developer = 3,
    Admin = 4,

    /// <summary>
    ///   This group does not allow anyone
    /// </summary>
    SystemOnly = 5,

    // Extension default group types
    SitePageEditor = 6,
    SiteLayoutEditor = 7,
    PostPublisher = 8,
    TemplateEditor = 9,
    SitePagePublisher = 10,
    WikiEditor = 11,
    PostEditor = 12,
    RedirectEditor = 13,

    /// <summary>
    ///   Supporter on Patreon with devbuild access
    /// </summary>
    PatreonSupporter = 14,

    // TODO: add association status (though maybe association status is fine to stay with the old system?)

    // All custom groups need to have an ID number above this
    Custom = 10000,

    // Disallowed max value
    Max = int.MaxValue,
}
