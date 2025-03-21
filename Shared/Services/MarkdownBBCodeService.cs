namespace RevolutionaryWebApp.Shared.Services;

using System;
using System.Text;
using System.Text.RegularExpressions;
using Models.Pages;

public class MarkdownBbCodeService : IMarkdownBbCodeService
{
    private readonly Regex likelyNeedToDoSomething =
        new(@"(\!\[[^\)\]]*\]\(media:)|([/\w+])", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly Regex mediaLinkToConvertRegex = new(
        @"\!\[([^\)\]]*)\]\(media:([\w\.]+):([a-f0-9\-]+)(\s*'[^']*')?\)",
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
            if (rawContent[i] == '[')
            {
                // Clear pending text to not cause issues when a tag is actually detected and processed
                if (hasStart)
                {
                    result.Append(rawContent.Substring(startIndex, i - startIndex));
                    hasStart = false;
                }

                if (TryHandleCustomTag(rawContent, ref i, result))
                    continue;
            }

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

    /// <summary>
    ///   Check if currently at a custom tag and if, so process it
    /// </summary>
    /// <returns>True if a custom tag was processed</returns>
    private bool TryHandleCustomTag(string rawContent, ref int i, StringBuilder result)
    {
        bool insideCustomTag = false;
        string? customTagName = null;

        // Outer already checks if '[' is current character so skip that one
        int j = i + 1;
        for (; j < rawContent.Length; ++j)
        {
            // Check if this is the start of a custom tag
            // By detecting when the tag name ends and checking the name
            if (rawContent[j] is not (']' or ' '))
                continue;

            // End of custom tag start. Check if the name is valid
            customTagName = rawContent.Substring(i + 1, j - i - 1);

            switch (customTagName)
            {
                case "puImage":
                    // This doesn't take parameters
                    if (rawContent[j] != ']')
                        return false;

                    break;
                default:
                    return false;
            }

            // We found a valid tag start
            insideCustomTag = true;
            break;
        }

        if (!insideCustomTag)
            return false;

        int startOfTagContent = j + 1;

        for (j = startOfTagContent; j < rawContent.Length; ++j)
        {
            // Check for the end of the tag
            // For now nested tags are not allowed, so we don't need a tag stack
            if (rawContent[j] != '[' || j + 1 >= rawContent.Length || rawContent[j + 1] != '/')
                continue;

            // End of custom tag
            int endOfTagData = j - 1;

            // Skip characters until the end of this tag
            while (j < rawContent.Length && rawContent[j] != ']')
            {
                ++j;
            }

            if (j >= rawContent.Length || rawContent[j] != ']')
            {
                // Failed to parse right at the end
                return false;
            }

            // Ensure the end of the tag matches the start
            // Skip the starting slash when comparing (this makes the numbers be 3 instead of 2 here)
            var endTag = rawContent.Substring(endOfTagData + 3, j - endOfTagData - 3);

            if (endTag != customTagName)
            {
                // Failed to match end tag
                return false;
            }

            if (startOfTagContent <= endOfTagData)
            {
                // Handle custom tag data
                switch (customTagName)
                {
                    case "puImage":
                    {
                        var date = rawContent.Substring(startOfTagContent, endOfTagData - startOfTagContent + 1);

                        // Do a simple format check before converting
                        if (date.IndexOf('-') == -1 || date.IndexOf(']') != -1 || date.Length > 20)
                            return false;

                        result.Append($"![Progress Update Banner {date}]" +
                            $"({mediaLinkConverter.GetGeneratedAndProxyImagePrefix()}/generated/puBanner/{date})");
                        break;
                    }

                    default:
                        throw new Exception("Logic error in parsing (unimplemented custom tag)");
                }
            }
            else
            {
                // Tag without content
                switch (customTagName)
                {
                    case "puBanner":
                        // Not valid without content
                        return false;
                }

                // TODO: remove when tags are added that support no content
                return false;
            }

            // Successfully processed a custom tag, adjust 'i' so that the outer caller will resume correctly
            i = j;
            return true;
        }

        // Wasn't a valid custom tag
        return false;
    }
}
