namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Dom;
using Controllers.Pages;
using Microsoft.Extensions.Configuration;
using Models;
using Models.Pages;
using Shared;
using Shared.Models.Pages;
using Shared.Services;
using SharedBase.Utilities;

public interface IPageRenderer
{
    internal const string NotFoundPageTitle = "404 - Not Found";
    internal const string NotFoundPageText = "<p>No page exists at this address, please double check the link</p>";

    public ValueTask<RenderedPage> RenderPage(VersionedPage page, string metadataBaseUrl,
        List<SiteLayoutPart> layoutParts, bool renderOpenGraphMeta, ApplicationDbContext? imageDatabase,
        Stopwatch totalTimer);

    public RenderedPage RenderNotFoundPage(List<SiteLayoutPart> layoutParts, Stopwatch totalTimer);

    /// <summary>
    ///   Creates a rendered preview of a page. Needs to have an expected base URL so that relative links work
    ///   correctly.
    /// </summary>
    /// <returns>
    ///   Rendered HTML and metadata for the first image, if the page has one.
    /// </returns>
    /// <remarks>
    ///   <para>
    ///     This is different from HtmlStringExtensions due to this removing many HTML element types that aren't good
    ///     to have in really short previews.
    ///   </para>
    /// </remarks>
    public (string Rendered, PagePreviewImage? PreviewImage)
        RenderPreview(VersionedPage page, string parsedUrlBase, string? readMoreLink, int targetMaxLength);

    public (List<RenderingLayoutPart>? Sidebar, List<RenderingLayoutPart>? TopNavigation, List<RenderingLayoutPart>?
        Socials) ProcessLayoutParts(List<SiteLayoutPart> layoutParts, string activeLink);
}

public sealed record PagePreviewImage(string Source, int? Width, int? Height);

public class PageRenderer : IPageRenderer
{
    private readonly MarkdownService markdownService;
    private readonly IMediaLinkConverter linkConverter;
    private readonly string? serverName;

    private readonly PageHtmlPipeline htmlPipeline;
    private readonly HtmlMarkupFormatter fragmentFormatter = new();

    public PageRenderer(IConfiguration configuration, MarkdownService markdownService,
        IMediaLinkConverter linkConverter)
    {
        this.markdownService = markdownService;
        this.linkConverter = linkConverter;
        serverName = configuration["ServerName"];
        htmlPipeline = new PageHtmlPipeline(new OpenGraphHtmlFilter(),
            new FullSizeImageHtmlFilter(configuration, linkConverter));
    }

    public async ValueTask<RenderedPage> RenderPage(VersionedPage page, string metadataBaseUrl,
        List<SiteLayoutPart> layoutParts, bool renderOpenGraphMeta, ApplicationDbContext? imageDatabase,
        Stopwatch totalTimer)
    {
        var rendered = markdownService.MarkdownToHtmlLimited(page.LatestContent);
        var htmlContext = new PageHtmlContext(renderOpenGraphMeta, imageDatabase);
        rendered = await htmlPipeline.Process(rendered, metadataBaseUrl, htmlContext);

        // Fallback to the title here should only happen when previewing
        var (sidebar, topNav, socials) = ProcessLayoutParts(layoutParts, page.Permalink ?? page.Title);

        var result = new RenderedPage(page.Title, rendered, page.UpdatedAt, totalTimer.Elapsed)
        {
            // TODO: control heading option in the versioned page
            ShowHeading = page.Permalink != AppInfo.IndexPermalinkName,

            // TODO: add option for this as well in the page
            ShowLogo = page.Permalink == AppInfo.IndexPermalinkName,

            TopNavigation = topNav,
            Sidebar = sidebar,
            Socials = socials,
        };

        if (!string.IsNullOrEmpty(serverName))
        {
            result.ByServer = serverName;
        }

        result.OpenGraphMetaDescription = htmlContext.OpenGraphDescription;
        result.PreviewImage = htmlContext.PreviewImage;

        return result;
    }

    public (string Rendered, PagePreviewImage? PreviewImage) RenderPreview(VersionedPage page, string parsedUrlBase,
        string? readMoreLink, int targetMaxLength)
    {
        // TODO: allow a special bbcode tag that overrides the page summary (needs to be removed in normal page
        // rendering)

        // TODO: Should have a separate mode that doesn't render advanced embeds like youtube, but just
        // has normal links.
        var rendered = markdownService.MarkdownToHtmlLimited(page.LatestContent);

        if (string.IsNullOrWhiteSpace(rendered))
            return ("(blank page)", null);

        var fragment = htmlPipeline.ParseBody(rendered, parsedUrlBase).ChildNodes;

        PagePreviewImage? previewImage = null;

        var stringBuilder = new StringBuilder();
        int limitLeft = targetMaxLength;

        var writer = new StringWriter(stringBuilder, CultureInfo.InvariantCulture);

        // Process the fragment nodes to trim them
        foreach (var node in fragment)
        {
            if (previewImage == null && OpenGraphHtmlFilter.FindPreviewImage(node) is { } image)
            {
                previewImage = new PagePreviewImage(image.Source ?? "missing-image-source",
                    ParseImageDimension(image.GetAttribute("width")),
                    ParseImageDimension(image.GetAttribute("height")));
            }

            if (HandleContentForPreview(node, ref limitLeft))
            {
                // Skip writing any elements that don't have any text
                if (!node.HasTextNodes())
                    continue;

                node.ToHtml(writer, fragmentFormatter);
            }
        }

        // Add a read more link if truncated like there is on WordPress
        if (limitLeft <= 0 && !string.IsNullOrEmpty(readMoreLink))
        {
            stringBuilder.Append($@" <a class=""more-link"" href=""{readMoreLink}"">Read More &#8594;</a>");
        }

        return (stringBuilder.ToString(), previewImage);
    }

    public RenderedPage RenderNotFoundPage(List<SiteLayoutPart> layoutParts, Stopwatch totalTimer)
    {
        var (sidebar, topNav, socials) = ProcessLayoutParts(layoutParts, "NOT FOUND");

        var result = new RenderedPage(IPageRenderer.NotFoundPageTitle,
            IPageRenderer.NotFoundPageText,
            DateTime.UtcNow,
            totalTimer.Elapsed)
        {
            ShowHeading = true,
            ShowLogo = true,

            Sidebar = sidebar,
            TopNavigation = topNav,
            Socials = socials,
        };

        if (!string.IsNullOrEmpty(serverName))
        {
            result.ByServer = serverName;
        }

        return result;
    }

    public (List<RenderingLayoutPart>? Sidebar, List<RenderingLayoutPart>? TopNavigation, List<RenderingLayoutPart>?
        Socials) ProcessLayoutParts(List<SiteLayoutPart> layoutParts, string activeLink)
    {
        List<RenderingLayoutPart>? top = null;
        List<RenderingLayoutPart>? side = null;
        List<RenderingLayoutPart>? socials = null;

        foreach (var layoutPart in layoutParts)
        {
            switch (layoutPart.PartType)
            {
                case SiteLayoutPartType.TopLink:
                    top ??= new List<RenderingLayoutPart>();
                    top.Add(layoutPart.GetRenderingData(activeLink, linkConverter));
                    break;
                case SiteLayoutPartType.Sidebar:
                    side ??= new List<RenderingLayoutPart>();
                    side.Add(layoutPart.GetRenderingData(activeLink, linkConverter));
                    break;
                case SiteLayoutPartType.SmallSocialsBar:
                    socials ??= new List<RenderingLayoutPart>();
                    socials.Add(layoutPart.GetRenderingData(activeLink, linkConverter));
                    break;
            }
        }

        return (side, top, socials);
    }

    private static int? ParseImageDimension(string? value)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size > 0 ?
            size :
            null;
    }

    private bool HandleContentForPreview(INode node, ref int limitLeft)
    {
        // Reach end if text length is too much when reaching this node
        if (limitLeft <= 0)
            return false;

        switch (node)
        {
            // Always removed elements
            case IHtmlImageElement:
            case IHtmlScriptElement:
            case IComment:
            case IHtmlAudioElement:
            case IHtmlAreaElement:
            case IHtmlButtonElement:
            case IHtmlCanvasElement:
            case IHtmlEmbedElement:
            case IHtmlFormElement:
            case IHtmlHeadElement:
            case IHtmlFieldSetElement:
            case IHtmlInlineFrameElement:
            case IHtmlInputElement:
            case IHtmlMarqueeElement:
            case IHtmlMediaElement:
            case IHtmlMenuElement:
            case IHtmlMenuItemElement:
            case IHtmlMetaElement:
            case IHtmlModElement:
            case IHtmlObjectElement:
            case IHtmlOptionElement:
            case IHtmlOptionsGroupElement:
            case IHtmlOutputElement:
            case IHtmlParamElement:
            case IHtmlPictureElement:
            case IHtmlProgressElement:
            case IHtmlSelectElement:
            case IHtmlSlotElement:
            case IHtmlSourceElement:
            case IHtmlStyleElement:
            case IHtmlTemplateElement:
            case IHtmlTextAreaElement:
            case IHtmlTimeElement:
            case IHtmlTitleElement:
            case IHtmlTrackElement:

            // Elements that arguably could stay in some contexts
            case IHtmlHrElement:
            {
                return false;
            }

            case IHtmlDivElement divElement:
            {
                // Skip YouTube videos
                if (divElement.ClassList.Contains("youtube-placeholder"))
                    return false;

                break;
            }

            // Elements that are trimmed (if too long)
            case IText text:
            {
                if (text.Data.Length > limitLeft)
                {
                    // Ellipsis is added later (as an HTML element), so this doesn't place the ellipsis
                    // There's a min length here so that the text isn't cut to have like just one character.
                    // Add the HTML ellipsis here so that it is placed within the body of the text and not in a
                    // separate section afterwards.
                    text.Data = text.Data.TruncateWithoutEllipsis(Math.Max(3, limitLeft)) + "\u2026";
                }

                limitLeft -= text.Data.Length;

                break;
            }

            case IHtmlHeadingElement headingElement:
            {
                if (string.IsNullOrWhiteSpace(headingElement.Title))
                    return false;

                if (headingElement.Title.Length > limitLeft)
                {
                    headingElement.Title =
                        headingElement.Title.TruncateWithoutEllipsis(Math.Max(3, limitLeft)) + "\u2026";
                }

                limitLeft -= headingElement.Title.Length;

                break;
            }

            default:
                // Remove a few characters for each tag from the preview length
                limitLeft -= 4;
                break;
        }

        foreach (var childNode in node.ChildNodes.ToList())
        {
            if (!HandleContentForPreview(childNode, ref limitLeft))
            {
                node.RemoveChild(childNode);
            }
            else
            {
                // Delete the child if there's no text left in it
                if (string.IsNullOrWhiteSpace(node.TextContent))
                    node.RemoveChild(childNode);
            }
        }

        return true;
    }
}
