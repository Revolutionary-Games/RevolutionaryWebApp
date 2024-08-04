namespace RevolutionaryWebApp.Shared.Models.Pages;

/// <summary>
///   Each media file has different sizes generated from it for preview etc.
/// </summary>
public enum MediaFileSize
{
    /// <summary>
    ///   Original size as uploaded
    /// </summary>
    Original,

    /// <summary>
    ///   Large size image, resized down to 2000x2000px
    /// </summary>
    Large,

    /// <summary>
    ///   Resized to fit page display, max width 900px
    /// </summary>
    FitPage,

    /// <summary>
    ///   Thumbnail for preview purposes in image lists, 128x128px max size
    /// </summary>
    Thumbnail,
}
