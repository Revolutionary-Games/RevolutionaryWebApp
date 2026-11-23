namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Configuration;
using Shared.Services;
using Utilities;

/// <summary>
///   Exports rendered post content to the BBCode supported by Steam announcements.
/// </summary>
public class SteamPostConverter
{
    private static readonly Regex UnsupportedBbCode = new(
        @"\[/?(?:center|steam|thriveItch|puImage)(?:[= ][^\]\r\n]*)?\]",
        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private static readonly Regex PairedBbCode = new(
        @"\[(?<tag>[a-z][a-z0-9]*)(?:[= ][^\]\r\n]*)?\](?<content>[\s\S]*?)\[/\k<tag>\]",
        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private readonly MarkdownService markdownService;
    private readonly Uri baseUrl;

    public SteamPostConverter(MarkdownService markdownService, IConfiguration configuration)
    {
        this.markdownService = markdownService;
        baseUrl = configuration.GetBaseUrl();
    }

    public string Convert(string markdown)
    {
        // Use the same renderer as published pages, including media and generated image URL resolution.
        var html = markdownService.MarkdownToHtmlLimited(markdown);

        // Remove unsupported source BBCode before generating any Steam tags. Repeating handles nested tags,
        // including tags whose contents contain Markdown-generated HTML elements.
        string previous;
        do
        {
            previous = html;
            html = PairedBbCode.Replace(html, "${content}");
        }
        while (html != previous);

        using var document = new HtmlParser().ParseDocument(UnsupportedBbCode.Replace(html, string.Empty));
        return RenderChildren(document.Body!).Trim();
    }

    private string RenderChildren(INode node)
    {
        var result = new StringBuilder();
        foreach (var child in node.ChildNodes)
        {
            // Ignore the HTML renderer's indentation between blocks, preserving spaces inside paragraphs.
            if (child is IText text && string.IsNullOrWhiteSpace(text.Data) &&
                node is IElement { LocalName: "body" or "ul" or "ol" or "blockquote" })
            {
                continue;
            }

            result.Append(Render(child));
        }

        return result.ToString();
    }

    private string Render(INode node)
    {
        if (node is IText text)
            return text.Data;

        if (node is not IElement element)
            return string.Empty;

        if (element.ClassList.Contains("youtube-placeholder"))
            return $"[previewyoutube={element.GetAttribute("data-video-id")};full][/previewyoutube]\n\n";

        switch (element.LocalName)
        {
            case "iframe":
            case "script":
            case "style":
                return string.Empty;
            case "img":
                return $"[img]{AbsoluteUrl(element.GetAttribute("src"))}[/img]";
            case "a":
                return $"[url={AbsoluteUrl(element.GetAttribute("href"))}]{RenderChildren(element)}[/url]";
            case "strong":
            case "b":
                return $"[b]{RenderChildren(element)}[/b]";
            case "em":
            case "i":
                return $"[i]{RenderChildren(element)}[/i]";
            case "del":
            case "s":
                return $"[strike]{RenderChildren(element)}[/strike]";
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
            {
                var level = Math.Min(element.LocalName[1] - '0', 3);
                return $"[h{level}]{RenderChildren(element)}[/h{level}]\n\n";
            }

            case "ul":
                return $"[list]\n{RenderChildren(element)}[/list]\n\n";
            case "ol":
                return $"[olist]\n{RenderChildren(element)}[/olist]\n\n";
            case "li":
                return $"[*]{RenderChildren(element).Trim()}\n";
            case "blockquote":
                return $"[quote]{RenderChildren(element).Trim()}[/quote]\n\n";
            case "pre":
                return $"[code]{element.TextContent.TrimEnd()}[/code]\n\n";
            case "code":
                return $"[code]{element.TextContent}[/code]";
            case "br":
                return "\n";
            case "hr":
                return "[hr][/hr]\n\n";
            case "p":
                return RenderChildren(element) + "\n\n";
            case "div":
            case "tr":
            {
                var content = RenderChildren(element).TrimEnd();
                return content.Length > 0 ? content + "\n\n" : string.Empty;
            }

            case "td":
            case "th":
                return RenderChildren(element) + "\t";
            default:
                return RenderChildren(element);
        }
    }

    private string AbsoluteUrl(string? url)
    {
        return Uri.TryCreate(baseUrl, url, out var absolute) ? absolute.AbsoluteUri : string.Empty;
    }
}
