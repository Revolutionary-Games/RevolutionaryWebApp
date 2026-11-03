namespace RevolutionaryWebApp.Shared.Models.Enums;

/// <summary>
///   API key scopes stored as comma-separated numbers. Values must never be reordered or renumbered;
///   append new values at the end.
/// </summary>
public enum APIKeyScope
{
    All = 0,
    Users = 1,
    Builds = 2,
    Storage = 3,
    ContinuousIntegration = 4,
    Servers = 5,
    Pages = 6,
    Translations = 7,
    Association = 8,
    Feeds = 9,
    Administration = 10,
    Launcher = 11,
    Notifications = 12,
    Comments = 13,
    Tools = 14,
    PublicInfo = 15,
    LFS = 16,
    Preferences = 17,
    DebugSymbols = 18,
    Reports = 19,
    CLA = 20,
    CIConfiguration = 21,
    SiteConfiguration = 22,
    Media = 23,
}
