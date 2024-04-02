namespace RevolutionaryWebApp.Server.Models.Pages;

using System.ComponentModel.DataAnnotations;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared;
using SharedBase.Utilities;

/// <summary>
///   A historical version of a page. Latest page content is in <see cref="VersionedPage.LatestContent"/> and that
///   would be version number one higher than the highest historical version
/// </summary>
[Index(nameof(PageId), nameof(Version), IsUnique = true, IsDescending = new[] { false, true })]
public class PageVersion : ISoftDeletable
{
    public PageVersion(long pageId, int version, string reverseDiff)
    {
        Version = version;
        ReverseDiff = reverseDiff;
        PageId = pageId;
    }

    public PageVersion(VersionedPage page, int version, string reverseDiff)
    {
        Version = version;
        ReverseDiff = reverseDiff;
        Page = page;
        PageId = page.Id;
    }

    public long PageId { get; set; }

    public VersionedPage Page { get; set; } = null!;

    public int Version { get; set; }

    [MaxLength(AppInfo.MaxPageEditCommentLength)]
    public string? EditComment { get; set; }

    public bool Deleted { get; set; }

    /// <summary>
    ///   A reverse diff to go from the newer version of the page to this version of the page
    /// </summary>
    [StringLength(AppInfo.MaxPageLength + GlobalConstants.KIBIBYTE)]
    public string ReverseDiff { get; set; }

    public User? EditedBy { get; set; }
    public long? EditedById { get; set; }
}
