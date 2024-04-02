namespace RevolutionaryWebApp.Server.Models.Pages;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models.Pages;

/// <summary>
///   Main model for all web pages, news posts etc.
/// </summary>
[Index(nameof(Permalink), IsUnique = true)]
[Index(nameof(Title), IsUnique = true)]
public class VersionedPage : UpdateableModel, ISoftDeletable
{
    public VersionedPage(string title)
    {
        Title = title;
    }

    [StringLength(120)]
    [AllowSortingBy]
    public string Title { get; set; }

    [StringLength(AppInfo.MaxPageLength)]
    public string LatestContent { get; set; } = string.Empty;

    // TODO: should this be here or in redis?
    // public string? CookedContent { get; set; }

    public PageVisibility Visibility { get; set; } = PageVisibility.HiddenDraft;

    /// <summary>
    ///   All pages must have an internal name they are accessed at with URLs
    /// </summary>
    public string? Permalink { get; set; }

    [AllowSortingBy]
    public DateTime? PublishedAt { get; set; }

    [MaxLength(AppInfo.MaxPageEditCommentLength)]
    public string? LastEditComment { get; set; }

    public bool Deleted { get; set; }

    public ICollection<PageVersion> PreviousVersions { get; set; } = new HashSet<PageVersion>();

    public ICollection<PageEditSuggestion> EditSuggestions { get; set; } = new HashSet<PageEditSuggestion>();

    public User? Creator { get; set; }
    public long? CreatorId { get; set; }

    public User? LastEditor { get; set; }
    public long? LastEditorId { get; set; }

    // TODO: implement
    // public static string PermalinkFromTitle()
    public VersionedPageDTO GetDTO(int currentVersion)
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Title = Title,
            LatestContent = LatestContent,
            Visibility = Visibility,
            Permalink = Permalink,
            PublishedAt = PublishedAt,
            LastEditComment = LastEditComment,
            VersionNumber = currentVersion,
            CreatorId = CreatorId,
            LastEditorId = LastEditorId,
        };
    }

    public async Task<int> GetCurrentVersion(ApplicationDbContext database)
    {
        var id = Id;
        var previousVersionNumber =
            await database.PageVersions.Where(v => v.PageId == id).MaxAsync(v => (int?)v.Version) ?? 0;

        // Current page version is always one higher than the previous one
        return previousVersionNumber + 1;
    }
}
