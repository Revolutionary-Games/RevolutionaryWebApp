namespace RevolutionaryWebApp.Shared.Models;

using System;
using Enums;

public class APIKeySettings
{
    public APIKeyScope[] Scopes { get; set; } = [APIKeyScope.All];
    public bool ReadOnly { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    public string? AllowedIPNetwork { get; set; }
}
