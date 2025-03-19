namespace RevolutionaryWebApp.Shared.Services;

using System.Diagnostics.CodeAnalysis;
using Models.Pages;

/// <summary>
///   Converts media links from an internal format to one that browsers can use
/// </summary>
public interface IMediaLinkConverter
{
    /// <summary>
    ///   Translates image links to final URLs to display images. This cannot be used to get the original size of the
    ///   image.
    /// </summary>
    /// <returns>A link to be used in an HTML image tag</returns>
    public string TranslateImageLink(string imageType, string globalId, MediaFileSize size);

    /// <summary>
    ///   Returns the prefix needed before a <c>/generated</c>, or <c>/imageProxy</c> links
    /// </summary>
    /// <returns>Prefix for</returns>
    public string GetGeneratedAndProxyImagePrefix();

    /// <summary>
    ///   Parses a media link to its image parts
    /// </summary>
    /// <param name="imageLink">
    ///   A raw link in the format "media:format:guid", for example, "media:png:c815f12d-604c-4e2d-a8ce-6b03992d0046"
    /// </param>
    /// <param name="imageType">The image type part of the link (with a leading dot)</param>
    /// <param name="imageGlobalId">Parsed image global ID</param>
    /// <returns>True on success</returns>
    public bool TryParseImageLink(string imageLink, [NotNullWhen(true)] out string? imageType,
        [NotNullWhen(true)] out string? imageGlobalId)
    {
        imageType = null;
        imageGlobalId = null;

        if (!imageLink.StartsWith("media:"))
            return false;

        var parts = imageLink.Split(':', 3);

        if (parts.Length != 3)
            return false;

        // Parts[0] should always be media thanks to the above check so that is skipped
        imageType = "." + parts[1];

        imageGlobalId = parts[2];

        // Could maybe check if the GUID is valid format

        return true;
    }
}
