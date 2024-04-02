namespace RevolutionaryWebApp.Shared.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class VersionedPageDTO : ClientSideTimedModel
{
    [Required]
    public string Title { get; set; } = null!;

    public PageVisibility Visibility { get; set; }

    public string? Permalink { get; set; }

    public DateTime? PublishedAt { get; set; }

    public long? CreatorId { get; set; }

    public long? LastEditorId { get; set; }

    [Required]
    [MaxLength(AppInfo.MaxPageLength)]
    public string LatestContent { get; set; } = null!;

    [MaxLength(AppInfo.MaxPageEditCommentLength)]
    public string? LastEditComment { get; set; }

    public int VersionNumber { get; set; }
}
