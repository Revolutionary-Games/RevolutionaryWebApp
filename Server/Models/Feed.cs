namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using System.Xml.Linq;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Converters;
using Shared.Models;
using Shared.Notifications;
using SmartFormat;
using Utilities;

/// <summary>
///   RSS feed to download from an external source
/// </summary>
[Index(nameof(Name), IsUnique = true)]
[Index(nameof(ContentUpdatedAt))]
public class Feed : FeedBase, ISoftDeletable, IUpdateNotifications, IDTOCreator<FeedDTO>, IInfoCreator<FeedInfo>
{
    public Feed(string url, string name, TimeSpan pollInterval) : base(name)
    {
        Url = url;
        Name = name;
        PollInterval = pollInterval;
    }

    [Required]
    [UpdateFromClientRequest]
    public string Url { get; set; }

    [Required]
    [UpdateFromClientRequest]
    public TimeSpan PollInterval { get; set; }

    /// <summary>
    ///   If specified overrides the cache time passed to readers of this feed data
    /// </summary>
    [UpdateFromClientRequest]
    public TimeSpan? CacheTime { get; set; }

    /// <summary>
    ///   If set to non-empty value a HTML mapped version of the feed data is available when queried with
    ///   <see cref="HtmlFeedVersionSuffix"/>
    /// </summary>
    [UpdateFromClientRequest]
    public string? HtmlFeedItemEntryTemplate { get; set; }

    /// <summary>
    ///   The Html version suffix, if empty then the default is html and to get the raw version another .suffix
    ///   needs to be used.
    /// </summary>
    [UpdateFromClientRequest]
    public string? HtmlFeedVersionSuffix { get; set; }

    /// <summary>
    ///   HTML content created based on the feed items
    /// </summary>
    public string? HtmlLatestContent { get; set; }

    /// <summary>
    ///   Max length of an item in the feed, too long items will be truncated
    /// </summary>
    [UpdateFromClientRequest]
    public int MaxItemLength { get; set; } = int.MaxValue;

    public string? PreprocessingActionsRaw;

    [NotMapped]
    [UpdateFromClientRequest]
    public List<FeedPreprocessingAction>? PreprocessingActions
    {
        get => PreprocessingActionsRaw != null ?
            JsonSerializer.Deserialize<List<FeedPreprocessingAction>>(PreprocessingActionsRaw) :
            null;
        set
        {
            PreprocessingActionsRaw = JsonSerializer.Serialize(value);
        }
    }

    public bool Deleted { get; set; }

    public ICollection<SeenFeedItem> SeenFeedItems { get; set; } = new HashSet<SeenFeedItem>();

    public ICollection<FeedDiscordWebhook> DiscordWebhooks { get; set; } = new HashSet<FeedDiscordWebhook>();

    public ICollection<CombinedFeed> CombinedInto { get; set; } = new HashSet<CombinedFeed>();

    public FeedInfo GetInfo()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Deleted = Deleted,
            Url = Url,
            Name = Name,
            PollInterval = PollInterval,
            CacheTime = CacheTime,
            ContentUpdatedAt = ContentUpdatedAt,
            PreprocessingActionsCount = PreprocessingActions?.Count ?? 0,
            HasHtmlFeedItemEntryTemplate = !string.IsNullOrEmpty(HtmlFeedItemEntryTemplate),
        };
    }

    public FeedDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Deleted = Deleted,
            Url = Url,
            Name = Name,
            PollInterval = PollInterval,
            CacheTime = CacheTime,
            MaxItems = MaxItems,
            MaxItemLength = MaxItemLength,
            LatestContentLength = LatestContent?.Length,
            ContentUpdatedAt = ContentUpdatedAt,
            PreprocessingActions = PreprocessingActions,
            HtmlFeedItemEntryTemplate = HtmlFeedItemEntryTemplate,
            HtmlFeedVersionSuffix = HtmlFeedVersionSuffix,
        };
    }

    /// <summary>
    ///   Just parses content for this feed and does nothing else
    /// </summary>
    /// <param name="rawContent">The feed content</param>
    /// <param name="modifiedDocument">Way to get out the processed feed data</param>
    /// <returns>Parsed items</returns>
    public List<ParsedFeedItem> ParseContent(string rawContent, out XDocument modifiedDocument)
    {
        var feedItems = new List<ParsedFeedItem>();

        modifiedDocument = XDocument.Parse(rawContent);

        var preprocessingActions = PreprocessingActions;

        foreach (var entry in modifiedDocument.Descendants().Where(e => e.Name.LocalName == "entry"))
        {
            if (preprocessingActions is { Count: > 0 })
                RunPreprocessingActions(entry, preprocessingActions);

            var id = entry.Descendants().FirstOrDefault(p => p.Name.LocalName == "id")?.Value;

            // Can't handle entries with no id
            if (id == null)
                continue;

            var link = entry.Descendants().FirstOrDefault(p => p.Name.LocalName == "link")?.Attribute("href")?.Value ??
                "Link is missing";
            var title = entry.Descendants().FirstOrDefault(p => p.Name.LocalName == "title")?.Value ?? "Unknown title";

            var authorNode = entry.Descendants().FirstOrDefault(p => p.Name.LocalName == "author");

            if (authorNode is { HasElements: true })
            {
                authorNode = authorNode.Descendants().FirstOrDefault(e => e.Name.LocalName == "name");
            }

            var author = authorNode?.Value ?? "Unknown author";

            var parsed = new ParsedFeedItem(id, EnsureNoDangerousContent(link), EnsureNoDangerousContent(title),
                EnsureNoDangerousContent(author))
            {
                Summary = EnsureNoDangerousContentMaybeNull(
                    entry.Descendants().FirstOrDefault(p => p.Name.LocalName == "summary")?.Value ??
                    entry.Descendants().FirstOrDefault(p => p.Name.LocalName == "content")?.Value),
                OriginalFeed = Name,
            };

            var published = entry.Descendants().FirstOrDefault(p => p.Name.LocalName == "published")?.Value;

            if (published != null && DateTime.TryParse(published, out var parsedTime))
            {
                parsed.PublishedAt = parsedTime.ToUniversalTime();
            }

            if (parsed.Summary != null && parsed.Summary.Length > MaxItemLength)
            {
                parsed.Summary = parsed.Summary.Truncate(MaxItemLength);
            }

            feedItems.Add(parsed);

            if (feedItems.Count >= MaxItems)
                break;
        }

        return feedItems;
    }

    /// <summary>
    ///   Processes raw feed content into this and stores it in <see cref="FeedBase.LatestContent"/> if the final content
    ///   changed
    /// </summary>
    /// <param name="rawContent">
    ///   The raw retrieved content. Should not be the error content if a feed returned non success status code.
    /// </param>
    /// <returns>List of feed items</returns>
    public IEnumerable<ParsedFeedItem> ProcessContent(string rawContent)
    {
        var feedItems = ParseContent(rawContent, out var document);

        // Write the clean document back out
        using var stream = new MemoryStream();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings()
        {
            Encoding = Encoding.UTF8,
            Indent = false,
        });
        document.WriteTo(writer);

        stream.Position = 0;
        var reader = new StreamReader(stream, Encoding.UTF8);
        var finalContent = reader.ReadToEnd();

        // Detect if the document changed and update our data
        if (finalContent != LatestContent)
        {
            LatestContent = finalContent;
            ContentUpdatedAt = DateTime.UtcNow;

            UpdateHtmlFeedContent(feedItems);
        }

        return feedItems;
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        // Skip sending normal updates if this is in deleted state (and didn't currently become undeleted
        // or deleted)
        if (entityState != EntityState.Modified || !Deleted)
        {
            yield return new Tuple<SerializedNotification, string>(
                new FeedListUpdated() { Item = GetInfo() }, NotificationGroups.FeedListUpdated);
        }

        yield return new Tuple<SerializedNotification, string>(
            new FeedUpdated() { Item = GetDTO() }, NotificationGroups.FeedUpdatedPrefix + Id);
    }

    private static void RunPreprocessingActions(XElement feedEntry, IEnumerable<FeedPreprocessingAction> actions)
    {
        var regexTimeout = TimeSpan.FromSeconds(5);

        foreach (var action in actions)
        {
            if (action.Target == PreprocessingActionTarget.Title)
            {
                var title = feedEntry.Descendants().FirstOrDefault(e => e.Name.LocalName == "title");

                if (title != null)
                {
                    title.Value = Regex.Replace(title.Value, action.ToFind, action.Replacer, RegexOptions.IgnoreCase,
                        regexTimeout);
                }
            }
            else if (action.Target == PreprocessingActionTarget.Summary)
            {
                var content = feedEntry.Descendants().FirstOrDefault(e => e.Name.LocalName == "content");

                if (content != null)
                {
                    content.Value = Regex.Replace(content.Value, action.ToFind, action.Replacer,
                        RegexOptions.IgnoreCase, regexTimeout);
                }

                content = feedEntry.Descendants().FirstOrDefault(e => e.Name.LocalName == "summary");

                if (content != null)
                {
                    content.Value = Regex.Replace(content.Value, action.ToFind, action.Replacer,
                        RegexOptions.IgnoreCase, regexTimeout);
                }
            }
            else
            {
                throw new ArgumentException("Unknown feed preprocessing action");
            }
        }
    }

    private static string EnsureNoDangerousContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        return EnsureNoDangerousContentMaybeNull(content)!;
    }

    private static string? EnsureNoDangerousContentMaybeNull(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return null;

        return content.Replace("<script>", "&lt;script&gt;");
    }

    private string CreateHtmlFeedContent(IEnumerable<ParsedFeedItem> items, string template)
    {
        var builder = new StringBuilder();

        foreach (var item in items)
        {
            builder.Append(Smart.Format(template, item.GetFormatterData(Name)));
        }

        return builder.ToString();
    }

    private void UpdateHtmlFeedContent(IEnumerable<ParsedFeedItem> items)
    {
        if (!string.IsNullOrEmpty(HtmlFeedItemEntryTemplate))
        {
            HtmlLatestContent = CreateHtmlFeedContent(items, HtmlFeedItemEntryTemplate);
        }
        else
        {
            HtmlLatestContent = null;
        }
    }
}

public class ParsedFeedItem
{
    public ParsedFeedItem(string id, string link, string title, string author)
    {
        Title = title;
        Author = author;
        Id = id;
        Link = link;
    }

    public string Id { get; set; }

    public string Link { get; set; }
    public string Title { get; set; }
    public string? Summary { get; set; }
    public string Author { get; set; }

    public DateTime PublishedAt { get; set; }

    public string? OriginalFeed { get; set; }

    public object GetFormatterData(string currentFeed)
    {
        return new
        {
            Id,
            Link,
            Title = HttpUtility.HtmlEncode(Title),
            Summary,
            PublishedAt,
            Author = HttpUtility.HtmlEncode(Author),
            AuthorFirstWord = HttpUtility.HtmlEncode(Author.Split(' ').First()),
            FeedName = HttpUtility.HtmlEncode(currentFeed),
            OriginalFeedName = HttpUtility.HtmlEncode(OriginalFeed),
        };
    }
}
