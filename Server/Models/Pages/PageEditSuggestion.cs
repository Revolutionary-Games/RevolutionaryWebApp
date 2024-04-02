namespace RevolutionaryWebApp.Server.Models.Pages;

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using SharedBase.Utilities;

/// <summary>
///   A pending suggestion to edit a page by a user who cannot edit it directly
/// </summary>
[Index(nameof(PageId), nameof(SuggestedById), IsUnique = true)]
public class PageEditSuggestion : UpdateableModel
{
    public PageEditSuggestion(long pageId, int appliesToPageVersion, string suggestedChangesDiff, long suggestedById)
    {
        PageId = pageId;
        AppliesToPageVersion = appliesToPageVersion;
        SuggestedChangesDiff = suggestedChangesDiff;
        SuggestedById = suggestedById;
    }

    public PageEditSuggestion(VersionedPage page, int appliesToPageVersion, string suggestedChangesDiff,
        User suggestedBy)
    {
        Page = page;
        PageId = page.Id;
        AppliesToPageVersion = appliesToPageVersion;
        SuggestedChangesDiff = suggestedChangesDiff;
        SuggestedBy = suggestedBy;
        SuggestedById = suggestedBy.Id;
    }

    public long PageId { get; set; }

    public VersionedPage Page { get; set; } = null!;

    /// <summary>
    ///   Score of votes by
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    ///   Used to know if an edit suggestion is outdated or not and if special care is needed
    /// </summary>
    public int AppliesToPageVersion { get; set; }

    [MaxLength(AppInfo.MaxPageEditCommentLength)]
    public string? EditComment { get; set; }

    /// <summary>
    ///   A diff of the suggested changes
    /// </summary>
    [StringLength(AppInfo.MaxPageLength + GlobalConstants.KIBIBYTE)]
    public string SuggestedChangesDiff { get; set; }

    public User SuggestedBy { get; set; } = null!;
    public long SuggestedById { get; set; }

    /// <summary>
    ///   A ';' separated list of upvoters. This is not saved in a normalized way in the DB as this is not super
    ///   critical info that needs to be cleared if a user votes on a thing.
    /// </summary>
    [MaxLength(GlobalConstants.MEBIBYTE)]
    public string? VotedUpBy { get; set; }

    /// <summary>
    ///   A ';' separated list of downvoters
    /// </summary>
    [MaxLength(GlobalConstants.MEBIBYTE)]
    public string? VotedDownBy { get; set; }
}
