namespace RevolutionaryWebApp.Server.Models;

using System;
using DevCenterCommunication.Models;

/// <summary>
///   Lighter variant of <see cref="ITimestampedModel"/> that only has an updated at timestamp
/// </summary>
public interface IHasUpdatedAt
{
    public DateTime UpdatedAt { get; set; }
}
