namespace RevolutionaryWebApp.Shared.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using SharedBase.ModelVerifiers;

public class VersionedPageDTO : ClientSideTimedModel
{
    [Required]
    [StringLength(AppInfo.MaxPageTitleLength, MinimumLength = 2)]
    [NoTrailingOrPrecedingSpace]
    public string Title { get; set; } = null!;

    public PageVisibility Visibility { get; set; }

    [MaxLength(AppInfo.MaxPagePermalinkLength)]
    [NoWhitespace]
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

    public PageType Type { get; set; }

    public bool Deleted { get; set; }

    public VersionedPageDTO Clone()
    {
        return new VersionedPageDTO
        {
            Id = Id,
            UpdatedAt = UpdatedAt,
            CreatedAt = CreatedAt,
            Title = Title,
            Permalink = Permalink,
            PublishedAt = PublishedAt,
            CreatorId = CreatorId,
            LastEditorId = LastEditorId,
            LatestContent = LatestContent,
            LastEditComment = LastEditComment,
            VersionNumber = VersionNumber,
            Type = Type,
            Deleted = Deleted,
        };
    }
}
