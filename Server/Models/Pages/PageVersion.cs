namespace RevolutionaryWebApp.Server.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models.Pages;
using SharedBase.Utilities;

/// <summary>
///   A historical version of a page. Latest page content is in <see cref="VersionedPage.LatestContent"/> and that
///   would be version number one higher than the highest historical version.
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

    [AllowSortingBy]
    public int Version { get; set; }

    [MaxLength(AppInfo.MaxPageEditCommentLength)]
    public string? EditComment { get; set; }

    [AllowSortingBy]
    public bool Deleted { get; set; }

    /// <summary>
    ///   A reverse diff to go from the newer version of the page to this version of the page. Gives a bit of room
    ///   on top of the page content length for JSON formatting
    /// </summary>
    [StringLength(AppInfo.MaxPageLength + GlobalConstants.KIBIBYTE * 32)]
    public string ReverseDiff { get; set; }

    public User? EditedBy { get; set; }

    [AllowSortingBy]
    public long? EditedById { get; set; }

    [AllowSortingBy]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DiffData DecodeDiffData()
    {
        return PageVersionDTO.DecodeDiffData(ReverseDiff);
    }

    public PageVersionInfo GetInfo()
    {
        return new PageVersionInfo
        {
            PageId = PageId,
            Version = Version,
            EditComment = EditComment,
            Deleted = Deleted,
            EditedById = EditedById,
            CreatedAt = CreatedAt,
        };
    }

    public PageVersionDTO GetDTO()
    {
        return new PageVersionDTO
        {
            PageId = PageId,
            Version = Version,
            EditComment = EditComment,
            Deleted = Deleted,
            ReverseDiffRaw = ReverseDiff,
            EditedById = EditedById,
            CreatedAt = CreatedAt,
        };
    }
}
