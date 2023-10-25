namespace ThriveDevCenter.Client.Services;

using Markdig;

/// <summary>
///   Provides markdown rendering with XSS sanitation
/// </summary>
public class MarkdownService
{
    private readonly HtmlSanitizerService sanitizer;

    private readonly MarkdownPipeline advancedPipeline;
    private readonly MarkdownPipeline basicPipeline;

    public MarkdownService(HtmlSanitizerService sanitizer)
    {
        this.sanitizer = sanitizer;

        // TODO: support for custom media link hosts

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
        var html = Markdown.ToHtml(markdown, advancedPipeline);

        if (!sanitizeHtml)
            return html;

        return sanitizer.SanitizeHtml(html);
    }

    public string MarkdownToHtmlLimited(string markdown, bool sanitizeHtml = true)
    {
        var html = Markdown.ToHtml(markdown, basicPipeline);

        if (!sanitizeHtml)
            return html;

        return sanitizer.SanitizeHtml(html);
    }
}
