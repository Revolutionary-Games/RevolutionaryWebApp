namespace RevolutionaryWebApp.Shared.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;

public class VersionedPageInfo : ClientSideTimedModel
{
    /// <summary>
    ///   Title of the page. This must have JSON name that matches <see cref="SoftDeletedResource.Name"/>.
    /// </summary>
    [Required]
    [JsonPropertyName("name")]
    public string Title { get; set; } = null!;

    public PageVisibility Visibility { get; set; }

    public string? Permalink { get; set; }

    public DateTime? PublishedAt { get; set; }

    public long? CreatorId { get; set; }

    public long? LastEditorId { get; set; }
}
