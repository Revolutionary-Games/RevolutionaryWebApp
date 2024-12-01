namespace RevolutionaryWebApp.Shared.Services;

using System;
using System.Text;
using System.Text.RegularExpressions;
using Models.Pages;

public class MarkdownBbCodeService : IMarkdownBbCodeService
{
    private readonly Regex likelyNeedToDoSomething =
        new(@"\!\[[^\)\]]*\]\(media:", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly Regex mediaLinkToConvertRegex = new(@"\!\[([^\)\]]*)\]\(media:(\w+):([a-f0-9\-]+)(\s*'[^']*')?\)",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

    private readonly IMediaLinkConverter mediaLinkConverter;

    public MarkdownBbCodeService(IMediaLinkConverter mediaLinkConverter)
    {
        this.mediaLinkConverter = mediaLinkConverter;
    }

    public string PreParseContent(string rawContent)
    {
        // If nothing needs to be done, then for efficiency this will just return the original string
        if (!likelyNeedToDoSomething.IsMatch(rawContent))
            return rawContent;

        var result = new StringBuilder(rawContent.Length);

        int startIndex = 0;
        bool hasStart = false;

        for (int i = 0; i < rawContent.Length; ++i)
        {
            if (rawContent[i] == '!' && IsMatch(rawContent, i, "!["))
            {
                // Handle currently pending text
                if (hasStart)
                {
                    result.Append(rawContent.Substring(startIndex, i - startIndex));
                    hasStart = false;
                }

                // Potential special link, check if valid
                var match = mediaLinkToConvertRegex.Match(rawContent, i);

                if (match.Success && match.Index == i)
                {
                    // Special handling for this image link content
                    var altText = match.Groups[1].Value;
                    var imageId = match.Groups[3].Value;
                    var imageType = match.Groups[2].Value;
                    var comment = match.Groups[4].Value;

                    if (comment.Length < 1)
                        comment = null;

                    if (string.IsNullOrWhiteSpace(imageType))
                        throw new Exception("Logic error in parsing");

                    if (imageType[0] != '.')
                        imageType = "." + imageType;

                    InsertImage(result,
                        mediaLinkConverter.TranslateImageLink(imageType, imageId, MediaFileSize.FitPage), altText,
                        comment);

                    // Skip input until the end of the replaced image
                    i += match.Length - 1;
                    continue;
                }
            }

            if (!hasStart)
            {
                hasStart = true;
                startIndex = i;
            }
        }

        if (hasStart)
        {
            result.Append(rawContent.Substring(startIndex));
        }

        return result.ToString();
    }

    public string PostProcessContent(string html)
    {
        // TODO: implement youtube embedding (with privacy friendly options to only view after accepting cookies)
        return html;
    }

    private static bool IsMatch(string input, int index, string pattern)
    {
        if (pattern.Length == 0)
            throw new ArgumentException("Pattern shouldn't be empty", nameof(pattern));

        // If possibly cannot be a match here, then fail
        if (input.Length < index + pattern.Length)
            return false;

        for (int i = index; i < input.Length; ++i)
        {
            var patternIndex = i - index;

            if (patternIndex >= pattern.Length)
                break;

            if (input[i] != pattern[patternIndex])
                return false;
        }

        return true;
    }

    private static void InsertImage(StringBuilder stringBuilder, string url, string altText, string? comment = null)
    {
        stringBuilder.Append("![");
        stringBuilder.Append(altText);
        stringBuilder.Append("](");
        stringBuilder.Append(url);

        if (comment != null)
        {
            if (!comment.StartsWith(' '))
                stringBuilder.Append(' ');

            stringBuilder.Append(comment);
        }

        stringBuilder.Append(')');
    }
}
