namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Enums;
using Notifications;
using SharedBase.Converters;

public class RealTimeBuildMessage : SerializedNotification
{
    [Required]
    [JsonConverter(typeof(ActualEnumStringConverter))]
    public BuildSectionMessageType Type { get; set; }

    public string? ErrorMessage { get; set; }

    public string? Output { get; set; }

    [MaxLength(100)]
    public string? SectionName { get; set; }

    public bool WasSuccessful { get; set; }

    /// <summary>
    ///   The section id (as we don't guarantee SectionName to be unique)
    /// </summary>
    public long SectionId { get; set; }
}
