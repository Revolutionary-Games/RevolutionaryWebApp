namespace RevolutionaryWebApp.Shared.Models;

using Enums;

/// <summary>
///   Details of the API key used to authenticate the current request and its owner.
/// </summary>
public class APIKeyInfo
{
    public APIKeyDTO Key { get; set; } = new();
    public long UserId { get; set; }
    public GroupType[] Groups { get; set; } = [];
}
