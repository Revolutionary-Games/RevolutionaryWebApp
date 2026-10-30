namespace RevolutionaryWebApp.Shared.Models;

using System.Text.Json.Serialization;
using SharedBase.Converters;

/// <summary>
///   How prominently a notification is displayed in the inbox.
/// </summary>
[JsonConverter(typeof(ActualEnumStringConverter))]
public enum NotificationPriority
{
    Low = -1,
    Normal = 0,
    High = 1,
}
