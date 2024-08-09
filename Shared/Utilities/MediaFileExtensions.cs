namespace RevolutionaryWebApp.Shared.Utilities;

using System;
using System.IO;
using Models.Pages;

public static class MediaFileExtensions
{
    public static string GetStoragePath(this IMediaFileInfo info, MediaFileSize size)
    {
        var name = info.Name;
        var extension = Path.GetExtension(name);

        var start = $"{info.GlobalId}/{Path.GetFileNameWithoutExtension(name)}";

        string sizeText;

        switch (size)
        {
            case MediaFileSize.Original:
                sizeText = string.Empty;
                break;
            case MediaFileSize.Large:
                sizeText = "_large";
                break;
            case MediaFileSize.FitPage:
                sizeText = "_fit";
                break;
            case MediaFileSize.Thumbnail:
                sizeText = "_thumb";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, null);
        }

        return start + sizeText + extension;
    }
}
