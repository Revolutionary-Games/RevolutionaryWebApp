namespace RevolutionaryWebApp.Server.Models.Pages;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Interfaces;
using Jobs.Pages;
using Microsoft.EntityFrameworkCore;
using Services;
using Shared;
using Shared.Models.Pages;
using Shared.Notifications;
using SharedBase.Utilities;
using Utilities;

/// <summary>
///   Main model for all web pages, news posts, etc.
/// </summary>
[Index(nameof(Permalink), IsUnique = true)]
[Index(nameof(Title), IsUnique = true)]
[Index(nameof(PublishedAt), nameof(Visibility))]
public class VersionedPage : UpdateableModel, ISoftDeletable, IUpdateNotifications
{
    public VersionedPage(string title)
    {
        Title = title;
    }

    [StringLength(AppInfo.MaxPageTitleLength)]
    [AllowSortingBy]
    [UpdateFromClientRequest]
    public string Title { get; set; }

    [StringLength(AppInfo.MaxPageLength)]
    [UpdateFromClientRequest(MaxLengthWhenDisplayingChanges = 100)]
    public string LatestContent { get; set; } = string.Empty;

    // TODO: should this be here or in redis?
    // public string? CookedContent { get; set; }

    [UpdateFromClientRequest]
    public PageVisibility Visibility { get; set; } = PageVisibility.HiddenDraft;

    // TODO: should this be able to change?
    public PageType Type { get; set; }

    /// <summary>
    ///   All pages must have an internal name they are accessed at with URLs
    /// </summary>
    [MaxLength(AppInfo.MaxPagePermalinkLength)]
    [UpdateFromClientRequest]
    public string? Permalink { get; set; }

    [AllowSortingBy]
    public DateTime? PublishedAt { get; set; }

    [MaxLength(AppInfo.MaxPageEditCommentLength)]
    [UpdateFromClientRequest]
    public string? LastEditComment { get; set; }

    public bool Deleted { get; set; }

    public ICollection<PageVersion> PreviousVersions { get; set; } = new HashSet<PageVersion>();

    public ICollection<PageEditSuggestion> EditSuggestions { get; set; } = new HashSet<PageEditSuggestion>();

    public User? Creator { get; set; }
    public long? CreatorId { get; set; }

    public User? LastEditor { get; set; }
    public long? LastEditorId { get; set; }

    public VersionedPageInfo GetInfo()
    {
        return new VersionedPageInfo
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            CreatorId = CreatorId,
            LastEditorId = LastEditorId,
            Permalink = Permalink,
            PublishedAt = PublishedAt,
            Title = Title,
            Visibility = Visibility,
        };
    }

    public VersionedPageDTO GetDTO(int currentVersion)
    {
        return new VersionedPageDTO
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
            Type = Type,
            Deleted = Deleted,
        };
    }

    public async Task<int> GetCurrentVersion(ApplicationDbContext database)
    {
        var id = Id;
        var previousVersionNumber =
            await database.PageVersions.Where(v => v.PageId == id).MaxAsync(v => (int?)v.Version) ?? 0;

        // The current page version is always one higher than the previous one
        return previousVersionNumber + 1;
    }

    /// <summary>
    ///   Must be called when this is edited. Clears all caches.
    /// </summary>
    public void OnEdited(IBackgroundJobClient jobClient)
    {
        jobClient.Enqueue<UpdatePageUsedMediaJob>(x => x.Execute(Id, CancellationToken.None));
        jobClient.Schedule<ClearPageCDNCacheJob>(x => x.Execute(Id, CancellationToken.None), TimeSpan.FromSeconds(15));
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        string singleGroup;
        string prefix;

        switch (Type)
        {
            case PageType.Template:
                singleGroup = NotificationGroups.PageTemplateListUpdated;
                prefix = NotificationGroups.PageTemplateUpdatedPrefix;
                break;
            case PageType.NormalPage:
                singleGroup = NotificationGroups.PageListUpdated;
                prefix = NotificationGroups.PageUpdatedPrefix;
                break;
            case PageType.Post:
                singleGroup = NotificationGroups.PostListUpdated;
                prefix = NotificationGroups.PostUpdatedPrefix;
                break;
            case PageType.WikiPage:
                singleGroup = NotificationGroups.WikiPageListUpdated;
                prefix = NotificationGroups.WikiPageUpdatedPrefix;
                break;

            default:
                yield break;
        }

        yield return new Tuple<SerializedNotification, string>(
            new PageListUpdated { Type = entityState.ToChangeType(), Item = GetInfo() },
            singleGroup);

        yield return new Tuple<SerializedNotification, string>(new VersionedPageUpdated { Item = GetDTO(-1) },
            prefix + Id);
    }

    public PageVersion CreatePreviousVersion(int currentVersion, string oldText)
    {
        var diff = DiffGenerator.Default.Generate(LatestContent, oldText);

        return new PageVersion(this, currentVersion,
            JsonSerializer.Serialize(diff, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    /// <summary>
    ///   Generates a preview of this page based on the first 1 or 2 paragraphs. Can contain HTML formatting.
    /// </summary>
    /// <returns>A preview of this item (HTML format, not plain text)</returns>
    public TextSyndicationContent GeneratePreview(IPageRenderer pageRenderer, Uri pageLink, out int usedMemory,
        int targetMaxLength = 350)
    {
        var (rendered, _) = pageRenderer.RenderPreview(this, $"{pageLink.Scheme}://{pageLink.Host}",
            pageLink.ToString(), targetMaxLength);

        var preview = new TextSyndicationContent(rendered, TextSyndicationContentKind.Html);

        usedMemory = rendered.Length;
        return preview;
    }

    public async Task<(TextSyndicationContent Content, int Length)> GenerateFullSyndicationContent(
        IPageRenderer pageRenderer, Uri pageLink)
    {
        var stopWatch = new Stopwatch();
        var rendered =
            await pageRenderer.RenderPage(this, $"{pageLink.Scheme}://{pageLink.Host}", [], false, stopWatch);

        var preview = new TextSyndicationContent(rendered.RenderedHtml, TextSyndicationContentKind.Html);

        var usedMemory = rendered.RenderedHtml.Length;
        return (preview, usedMemory);
    }

    public TimeSpan CalculatedDesiredCacheTime()
    {
        var timeSinceUpdate = DateTime.UtcNow - UpdatedAt;

        // Progressively increasing cache time when the page is older. We should have a working cache clean but just
        // for safety new pages that might need a quick fix aren't cached for super long
        if (timeSinceUpdate < TimeSpan.FromMinutes(10))
        {
            return TimeSpan.FromMinutes(2);
        }

        if (timeSinceUpdate < TimeSpan.FromMinutes(30))
        {
            return TimeSpan.FromMinutes(10);
        }

        if (timeSinceUpdate < TimeSpan.FromHours(12))
        {
            return TimeSpan.FromHours(1);
        }

        if (timeSinceUpdate < TimeSpan.FromDays(1))
        {
            return TimeSpan.FromHours(4);
        }

        if (timeSinceUpdate < TimeSpan.FromDays(8))
        {
            return TimeSpan.FromDays(1);
        }

        return AppInfo.MaxPageCacheTime;
    }

    public string GetOpenGraphType()
    {
        if (Type == PageType.NormalPage)
            return "website";

        return "article";
    }
}
