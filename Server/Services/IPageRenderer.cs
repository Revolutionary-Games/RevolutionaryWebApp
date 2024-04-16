namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Controllers.Pages;
using Microsoft.Extensions.Configuration;
using Models.Pages;
using Shared;
using Shared.Services;

public interface IPageRenderer
{
    internal const string NotFoundPageTitle = "404 - Not Found";
    internal const string NotFoundPageText = "<p>No page exists at this address, please double check the link</p>";

    public ValueTask<RenderedPage> RenderPage(VersionedPage page, Stopwatch totalTimer);
    public RenderedPage RenderNotFoundPage(Stopwatch totalTimer);
}

public class PageRenderer : IPageRenderer
{
    private readonly MarkdownService markdownService;
    private readonly string? serverName;

    public PageRenderer(IConfiguration configuration, MarkdownService markdownService)
    {
        this.markdownService = markdownService;
        serverName = configuration["ServerName"];
    }

    public ValueTask<RenderedPage> RenderPage(VersionedPage page, Stopwatch totalTimer)
    {
        // TODO: handle storage access links etc. this will need async

        var rendered = markdownService.MarkdownToHtmlLimited(page.LatestContent);

        // TODO: Youtube? and other special bbcode stuff

        // TODO: post processing? (stuff like rel no follow)

        var result = new RenderedPage(page.Title, rendered, page.UpdatedAt, totalTimer.Elapsed)
        {
            // TODO: control heading option in the versioned page
            ShowHeading = true,

            // TODO: add option for this as well in the page
            ShowLogo = page.Permalink == AppInfo.IndexPermalinkName,
        };

        if (!string.IsNullOrEmpty(serverName))
        {
            result.ByServer = serverName;
        }

        return ValueTask.FromResult(result);
    }

    public RenderedPage RenderNotFoundPage(Stopwatch totalTimer)
    {
        var result = new RenderedPage(IPageRenderer.NotFoundPageTitle, IPageRenderer.NotFoundPageText, DateTime.UtcNow,
            totalTimer.Elapsed)
        {
            ShowHeading = true,
            ShowLogo = true,
        };

        if (!string.IsNullOrEmpty(serverName))
        {
            result.ByServer = serverName;
        }

        return result;
    }
}
