namespace RevolutionaryWebApp.Shared.Services;

using System.Linq;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Utilities;

/// <summary>
///   Provides markdown rendering with XSS sanitation
/// </summary>
public class MarkdownService
{
    private readonly HtmlSanitizerService sanitizer;
    private readonly IMarkdownBbCodeService markdownBbCodeService;

    private readonly MarkdownPipeline advancedPipeline;
    private readonly MarkdownPipeline basicPipeline;

    public MarkdownService(HtmlSanitizerService sanitizer, IMarkdownBbCodeService markdownBbCodeService)
    {
        this.sanitizer = sanitizer;
        this.markdownBbCodeService = markdownBbCodeService;

        // TODO: support for custom media link hosts

        // TODO: mermaid diagrams seem way too hard to get working so it's enabled here but won't work
        advancedPipeline = new MarkdownPipelineBuilder().UseEmojiAndSmiley().UseSmartyPants().UseAdvancedExtensions()
            .Build();

        basicPipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras()
            .UseAutoLinks()
            .UseFootnotes()
            .UseMediaLinks()
            .UseCitations()
            .UsePipeTables()
            .UseGridTables()
            .UseListExtras()
            .UseEmojiAndSmiley()
            .UseSmartyPants()
            .UseMathematics()
            .Build();
    }

    public string MarkdownToHtmlWithAllFeatures(string markdown, bool sanitizeHtml = true)
    {
        var html = Render(markdown, advancedPipeline);

        if (!sanitizeHtml)
            return markdownBbCodeService.PostProcessContent(html);

        return markdownBbCodeService.PostProcessContent(sanitizer.SanitizeHtml(html));
    }

    public string MarkdownToHtmlLimited(string markdown, bool sanitizeHtml = true)
    {
        var html = Render(markdown, basicPipeline);

        if (!sanitizeHtml)
            return markdownBbCodeService.PostProcessContent(html);

        return markdownBbCodeService.PostProcessContent(sanitizer.SanitizeHtml(html));
    }

    private string Render(string markdown, MarkdownPipeline pipeline)
    {
        var document = Markdown.Parse(markdownBbCodeService.PreParseContent(markdown), pipeline);
        foreach (var literal in document.Descendants<LiteralInline>().ToList())
        {
            // Links (including image labels) already have a destination.
            bool insideLink = false;
            for (var parent = literal.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is LinkInline)
                    insideLink = true;
            }

            if (insideLink)
                continue;

            // Skip anything that cannot have mentions
            if (!literal.Content.Match("@"))
                continue;

            // So that we don't have to convert a bunch of useless stuff to strings
            var text = literal.Content.ToString();
            int offset = 0;
            foreach (System.Text.RegularExpressions.Match match in Usernames.MentionPattern.Matches(text))
            {
                if (match.Index > offset)
                    literal.InsertBefore(new LiteralInline(text.Substring(offset, match.Index - offset)));

                var link = new LinkInline("/profiles/" + match.Groups[1].Value, string.Empty);
                link.AppendChild(new LiteralInline(match.Value));
                literal.InsertBefore(link);
                offset = match.Index + match.Length;
            }

            if (offset == 0)
                continue;

            if (offset < text.Length)
                literal.InsertBefore(new LiteralInline(text.Substring(offset)));
            literal.Remove();
        }

        return document.ToHtml(pipeline);
    }
}
