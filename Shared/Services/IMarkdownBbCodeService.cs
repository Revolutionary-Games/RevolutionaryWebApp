namespace RevolutionaryWebApp.Shared.Services;

/// <summary>
///   Adds Thrive-customized bbcode parsing to extend markdown with some important features for pages
/// </summary>
public interface IMarkdownBbCodeService
{
    /// <summary>
    ///   Handle content before feeding it to the Markdown parser
    /// </summary>
    /// <returns>New text with pre-processing applied</returns>
    public string PreParseContent(string rawContent);

    /// <summary>
    ///   Performs post-processing on the already sanitized HTML to put back in HTML parts that shouldn't be prevented
    ///   by the sanitizer
    /// </summary>
    /// <param name="html">HTML code that is sanitized already to post-process it</param>
    /// <returns>Further processed HTML</returns>
    public string PostProcessContent(string html);
}
