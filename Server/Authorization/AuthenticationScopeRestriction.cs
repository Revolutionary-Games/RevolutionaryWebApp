namespace ThriveDevCenter.Server.Authorization;

/// <summary>
///   Might be better to use claims or something, but for now there's just 3 levels of access when
///   user is authenticated.
///   TODO: actually this shouldn't be needed if the authentication middlewares are properly scoped by path
/// </summary>
public enum AuthenticationScopeRestriction
{
    None,

    /// <summary>
    ///   Authenticated with LFS token, only valid for LFS access
    /// </summary>
    LFSOnly,

    /// <summary>
    ///   Authenticated through a launcher link, only valid for launcher endpoint
    /// </summary>
    LauncherOnly,
}