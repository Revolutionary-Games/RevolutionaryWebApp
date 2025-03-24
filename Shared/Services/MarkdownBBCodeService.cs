namespace RevolutionaryWebApp.Shared.Services;

using System;
using System.Text;
using System.Text.RegularExpressions;
using Models.Pages;

public class MarkdownBbCodeService : IMarkdownBbCodeService
{
    private readonly Regex likelyNeedToDoSomething =
        new(@"(\!\[[^\)\]]*\]\(media:)|([/\w+])|(\]\(page:)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly Regex mediaLinkToConvertRegex = new(
        @"\!\[([^\)\]]*)\]\(media:([\w\.]+):([a-f0-9\-]+)(\s*'[^']*')?\)",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

    private readonly Regex youtubeMarkdownRegex = new(@"\[youtube\](\w+)\[/youtube\]", RegexOptions.Compiled);

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

        void FlushPending(int i)
        {
            if (hasStart)
            {
                result.Append(rawContent.Substring(startIndex, i - startIndex));
                hasStart = false;
            }
        }

        for (int i = 0; i < rawContent.Length; ++i)
        {
            if (rawContent[i] == '[')
            {
                // Clear pending text to not cause issues when a tag is actually detected and processed
                FlushPending(i);

                if (TryHandleCustomTag(rawContent, ref i, result))
                    continue;
            }

            if (rawContent[i] == '!' && IsMatch(rawContent, i, "!["))
            {
                // Handle currently pending text
                FlushPending(i);

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
            else if (rawContent[i] == '(' && i > 0 && rawContent[i - 1] == ']')
            {
                if (IsMatch(rawContent, i + 1, "page:"))
                {
                    FlushPending(i);

                    result.Append('(');

                    // Add the link prefix
                    result.Append(mediaLinkConverter.GetInternalPageLinkPrefix());
                    result.Append('/');

                    // And then let the normal character copying handle the actual link content but skipping the data
                    // we handled already, which is length of ```(page:```
                    i += 6 - 1;
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
        StringBuilder? result = null;

        // Detect and handle YouTube
        var matches = youtubeMarkdownRegex.Matches(html);
        for (int i = 0; i < matches.Count; ++i)
        {
            var match = matches[i];
            var videoId = match.Groups[1].Value;

            // ReSharper disable once CommentTypo
            // The video is invalid if it has dots in the name or `youtu.be`
            // Resharper disable once StringLiteralTypo
            if (videoId.Contains('.') || videoId.Contains("youtu.be"))
                continue;

            // Initialize data conversion if needed
            result ??= new StringBuilder(html);

            // TODO: should we try to remove like containing <p> elements?
            result.Replace(match.Value, GenerateYoutubeEmbedCode(videoId));
        }

        return result?.ToString() ?? html;
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

    private string GenerateYoutubeEmbedCode(string videoId)
    {
        var prefix = mediaLinkConverter.GetGeneratedAndProxyImagePrefix();

        return $"""
                <div class="youtube-placeholder" data-video-id="{videoId}">
                    <div class="thumbnail-container">
                        <img class="youtube-thumbnail" alt="YouTube Video Thumbnail" src="{prefix}/imageProxy/youtubeThumbnail/{videoId}"/>
                        <div class="play-button-overlay">
                            <div class="css-play-button"></div>
                        </div>
                    </div>
                    <div class="youtube-controls">
                        <div class="controls-row">
                            Viewing embedded videos on this page uses YouTube cookies.
                            You accept these third-party and tracking cookies by clicking play.
                        </div>
                        <div class="controls-row">
                            <a class="youtube-btn open-youtube" href="https://www.youtube.com/watch?v={videoId}" target="_blank">View on YouTube</a>
                            <button class="youtube-btn accept-cookies">Always Accept YouTube Cookies</button>
                        </div>
                    </div>
                    <div class="youtube-embed-container" style="display: none;"></div>
                </div><div class="youtube-placeholder-below"></div>
                """;
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
