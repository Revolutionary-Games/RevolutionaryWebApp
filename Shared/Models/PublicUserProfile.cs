namespace RevolutionaryWebApp.Shared.Models;

using System;
using DevCenterCommunication.Models;

public class PublicUserProfile : IIdentifiable
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? DisplayName { get; set; }
}
