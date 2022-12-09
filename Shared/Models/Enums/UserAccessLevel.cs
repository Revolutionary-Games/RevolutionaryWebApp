namespace ThriveDevCenter.Shared.Models.Enums;

/// <summary>
///   The level of access a given user (or no user) has to the system
/// </summary>
public enum UserAccessLevel
{
    NotLoggedIn = 0,
    RestrictedUser = 1,
    User = 2,
    Developer = 3,
    Admin = 4,
}
