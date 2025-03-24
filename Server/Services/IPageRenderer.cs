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
using AngleSharp.Html.Parser;
using Controllers.Pages;
using Microsoft.Extensions.Configuration;
using Models.Pages;
using Shared;
using Shared.Models.Pages;
using Shared.Services;
using SharedBase.Utilities;

public interface IPageRenderer
{
    internal const string NotFoundPageTitle = "404 - Not Found";
    internal const string NotFoundPageText = "<p>No page exists at this address, please double check the link</p>";

    public ValueTask<RenderedPage> RenderPage(VersionedPage page, List<SiteLayoutPart> layoutParts,
        bool renderOpenGraphMeta, Stopwatch totalTimer);

    public RenderedPage RenderNotFoundPage(List<SiteLayoutPart> layoutParts, Stopwatch totalTimer);

    /// <summary>
    ///   Creates a rendered preview of a page
    /// </summary>
    /// <returns>
    ///   Tuple of rendered HTML and a link to the first image (used as preview banner image) if there is one in the
    ///   page, otherwise null and site default banner should be used
    /// </returns>
    /// <remarks>
    ///   <para>
    ///     This is different from HtmlStringExtensions due to this removing many HTML element types that aren't good
    ///     to have in really short previews.
    ///   </para>
    /// </remarks>
    public (string Rendered, string? PreviewImage)
        RenderPreview(VersionedPage page, string? readMoreLink, int targetMaxLength);
}

public class PageRenderer : IPageRenderer
{
    private readonly MarkdownService markdownService;
    private readonly IMediaLinkConverter linkConverter;
    private readonly string? serverName;

    private readonly HtmlParser htmlParser = new();
    private readonly HtmlMarkupFormatter fragmentFormatter = new();

    // TODO: is this safe to have as a shared variable? (this is a singleton service)
    private readonly IElement partialFragmentContext;

    public PageRenderer(IConfiguration configuration, MarkdownService markdownService,
        IMediaLinkConverter linkConverter)
    {
        this.markdownService = markdownService;
        this.linkConverter = linkConverter;
        serverName = configuration["ServerName"];

        partialFragmentContext = htmlParser.ParseDocument(string.Empty).DocumentElement;
    }

    public ValueTask<RenderedPage> RenderPage(VersionedPage page, List<SiteLayoutPart> layoutParts,
        bool renderOpenGraphMeta, Stopwatch totalTimer)
    {
        // TODO: handle storage access links etc. this will need async

        var rendered = markdownService.MarkdownToHtmlLimited(page.LatestContent);

        // TODO: Youtube? and other special bbcode stuff

        // TODO: post processing? (stuff like rel no follow for normal user-specified content)

        string? description = null;
        string? image = null;
        if (renderOpenGraphMeta)
        {
            (description, image) = RenderOpenGraphMetaDescription(rendered);
        }

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

        if (renderOpenGraphMeta)
        {
            result.OpenGraphMetaDescription = description;
            result.PreviewImage = image;
        }

        return ValueTask.FromResult(result);
    }

    public (string Rendered, string? PreviewImage) RenderPreview(VersionedPage page,
        string? readMoreLink,
        int targetMaxLength)
    {
        // TODO: allow a special bbcode tag that overrides the page summary (needs to be removed in normal page
        // rendering)

        // TODO: special handling mode for youtube and other advanced links

        // TODO: render the content as HTML and then grab the first portion to be the preview (but remove any
        // embedded images). Should have a separate mode that doesn't render advanced embeds like youtube, but just
        // has normal links.
        var rendered = markdownService.MarkdownToHtmlLimited(page.LatestContent);

        // Grab up to max length data from the rendered HTML (and remove unwanted elements)
        // TODO: is there a way to do this *without* needing to parse the data?
        var fragment = htmlParser.ParseFragment(rendered, partialFragmentContext);

        string? previewImage = null;

        var stringBuilder = new StringBuilder();
        int limitLeft = targetMaxLength;

        var writer = new StringWriter(stringBuilder, CultureInfo.InvariantCulture);

        // Process the fragment nodes to trim them
        foreach (var node in fragment)
        {
            if (previewImage == null)
                LookForPreviewImage(node, ref previewImage);

            if (HandleContentForPreview(node, ref limitLeft))
            {
                // We don't want this to be written in the output, so only output the children
                if (node is IHtmlBodyElement)
                {
                    foreach (var childNode in node.ChildNodes)
                    {
                        childNode.ToHtml(writer, fragmentFormatter);
                    }
                }
                else
                {
                    node.ToHtml(writer, fragmentFormatter);
                }
            }
        }

        if (limitLeft <= 0)
        {
            // This is the three ellipsis character in html-encoded form
            stringBuilder.Append("&#8230; ");
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

    private static bool LookForPreviewImage(INode node, ref string? previewImage)
    {
        switch (node)
        {
            case IHtmlImageElement imgImageElement:
            {
                if (!string.IsNullOrEmpty(imgImageElement.Source))
                {
                    previewImage = imgImageElement.Source;
                    return true;
                }

                break;
            }
        }

        foreach (var childNode in node.ChildNodes)
        {
            if (LookForPreviewImage(childNode, ref previewImage))
                return true;
        }

        return false;
    }

    private (List<RenderingLayoutPart>? Sidebar, List<RenderingLayoutPart>? TopNavigation, List<RenderingLayoutPart>?
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

    /// <summary>
    ///   Creates an opengraph meta-tag description for a page (the page needs to be rendered first)
    /// </summary>
    /// <returns>Opengraph meta-information</returns>
    private (string Description, string? PreviewImage) RenderOpenGraphMetaDescription(string rendered,
        int maxLength = 155)
    {
        // TODO: skipping youtube and bigger elements would be really nice to skip text from them leaking into the
        // summary

        // TODO: maybe there's another sensible approach (like extracting text from the raw markdown for opengraph)
        var fragment = htmlParser.ParseFragment(rendered, partialFragmentContext);

        string? previewImage = null;

        var stringBuilder = new StringBuilder();

        // Process the fragment nodes to extract preview text from them
        foreach (var node in fragment)
        {
            if (previewImage == null)
                LookForPreviewImage(node, ref previewImage);

            if (!HandleContentForOpenGraph(node, maxLength, stringBuilder))
                break;
        }

        return (stringBuilder.ToString(), previewImage);
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

            // Elements that are trimmed (if too long)
            case IText text:
            {
                if (text.Data.Length < limitLeft)
                {
                    // Ellipsis is added later (as an HTML element) so this doesn't place the ellipsis
                    // There's a min length here so that the text isn't cut to have like just one character
                    text.Data = text.Data.TruncateWithoutEllipsis(Math.Max(3, limitLeft));
                }

                limitLeft -= text.Data.Length;

                break;
            }

            case IHtmlHeadingElement headingElement:
            {
                if (string.IsNullOrWhiteSpace(headingElement.Title))
                    return false;

                if (headingElement.Title.Length < limitLeft)
                {
                    headingElement.Title = headingElement.Title.TruncateWithoutEllipsis(Math.Max(3, limitLeft));
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
        }

        return true;
    }

    private bool HandleContentForOpenGraph(INode node, int maxLength, StringBuilder stringBuilder)
    {
        // Reach end if text length is too much when reaching this node
        if (stringBuilder.Length >= maxLength)
            return false;

        switch (node)
        {
            // Elements that we don't want to extract text from
            // TODO: some of the following should probably end the snippet if there's more than half text already
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
            case IHtmlHrElement:
            {
                return true;
            }

            // Elements that have text parts extracted
            case IText text:
            {
                if (string.IsNullOrWhiteSpace(text.Data))
                    break;

                int remainingLength = maxLength - stringBuilder.Length;

                // Add whitespace between text parts
                if (stringBuilder.Length > 0 && !char.IsWhiteSpace(stringBuilder[^1]))
                {
                    stringBuilder.Append(' ');
                    --remainingLength;
                }

                // Stop processing if there wasn't any text left to extract
                if (remainingLength <= 0)
                    break;

                stringBuilder.Append(text.Data.Trim().Truncate(remainingLength));

                break;
            }

            case IHtmlHeadingElement headingElement:
            {
                // End if there's already a good amount of text
                if (stringBuilder.Length > 0.3f * maxLength)
                    return false;

                if (string.IsNullOrWhiteSpace(headingElement.Title))
                    break;

                int remainingLength = maxLength - stringBuilder.Length;

                if (stringBuilder.Length > 0 && !char.IsWhiteSpace(stringBuilder[^1]))
                {
                    stringBuilder.Append(' ');
                    --remainingLength;
                }

                stringBuilder.Append(headingElement.Title.ToUpperInvariant().Trim().Truncate(remainingLength));

                break;
            }

            case IHtmlDivElement divElement:
            {
                // Skip YouTube videos
                if (divElement.ClassList.Contains("youtube-placeholder"))
                    return true;

                break;
            }
        }

        foreach (var childNode in node.ChildNodes.ToList())
        {
            if (!HandleContentForOpenGraph(childNode, maxLength, stringBuilder))
            {
                return false;
            }
        }

        return true;
    }
}
