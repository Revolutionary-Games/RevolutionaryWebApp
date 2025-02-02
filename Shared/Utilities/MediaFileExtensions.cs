namespace RevolutionaryWebApp.Shared.Utilities;

using System;
using System.IO;
using Models.Pages;

public static class MediaFileExtensions
{
    public static string GetStoragePath(this IMediaFileInfo info, MediaFileSize size)
    {
        var name = info.Name;

        if (size != MediaFileSize.Original)
        {
            var extension = Path.GetExtension(name);

            // Thumbnails override the extension
            if (size == MediaFileSize.Thumbnail)
                extension = AppInfo.MediaPreviewFileExtension;

            return GetViewPath(info.GlobalId.ToString(), size, extension);
        }

        return $"{info.GlobalId}/{name}";
    }

    /// <summary>
    ///   To facilitate easy linking, the other sizes except the original don't have the original file name in them
    /// </summary>
    /// <returns>Last part of the link to this media resource</returns>
    /// <exception cref="ArgumentException">If original ize is attempted to be read</exception>
    public static string GetViewPath(string globalId, MediaFileSize size, string extension)
    {
        if (size == MediaFileSize.Original)
            throw new ArgumentException("Original size requires name to be known");

        if (string.IsNullOrEmpty(extension) || extension[0] != '.')
            throw new ArgumentException("Extension can't be empty or not start with a dot");

        string sizeText;

        switch (size)
        {
            case MediaFileSize.Large:
                sizeText = "large";
                break;
            case MediaFileSize.FitPage:
                sizeText = "page-fit";
                break;
            case MediaFileSize.Thumbnail:
                sizeText = "thumb";

                // Thumbnails are always a certain file type
                extension = AppInfo.MediaPreviewFileExtension;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, null);
        }

        return $"{globalId}/{sizeText}{extension}";
    }
}
