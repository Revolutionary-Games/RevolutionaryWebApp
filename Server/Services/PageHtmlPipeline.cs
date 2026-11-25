namespace RevolutionaryWebApp.Server.Services;

using System.Net;
using System.Threading.Tasks;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Models;

internal class PageHtmlPipeline(params IPageHtmlFilter[] filters)
{
    private readonly HtmlParser parser = new();

    public async ValueTask<string> Process(string html, string baseUrl, PageHtmlContext context)
    {
        IHtmlElement? body = null;
        bool changed = false;

        foreach (var filter in filters)
        {
            if (!filter.WantsToProcess(html, context))
                continue;

            body ??= ParseBody(html, baseUrl);
            changed |= await filter.Process(body, context);
        }

        return changed ? body!.InnerHtml : html;
    }

    public IHtmlElement ParseBody(string html, string baseUrl)
    {
        // Give the parser a base URL so image properties resolve relative links.
        var document = parser.ParseDocument(
            $"""
             <!DOCTYPE html><html><head><base href="{WebUtility.HtmlEncode(baseUrl)}"></head>
             <body>{html}</body></html>
             """);
        return document.Body!;
    }
}

internal class PageHtmlContext(bool renderOpenGraphMeta, ApplicationDbContext? imageDatabase)
{
    public bool RenderOpenGraphMeta { get; } = renderOpenGraphMeta;
    public ApplicationDbContext? ImageDatabase { get; } = imageDatabase;
    public string? OpenGraphDescription { get; set; } = renderOpenGraphMeta ? "(blank page)" : null;
    public string? PreviewImage { get; set; }
}
