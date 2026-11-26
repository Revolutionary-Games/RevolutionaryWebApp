namespace RevolutionaryWebApp.Server.Utilities;

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Models.Pages;
using Services;
using Shared;
using Shared.Models.Pages;
using Shared.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

internal static class MediaImagePreviews
{
    public static Image CreatePageImage(Image image)
    {
        return ImageHelpers.DownSampleImageIfLargerThan(image, AppInfo.MediaResolutionPage,
            AppInfo.MediaResolutionPage, KnownResamplers.Lanczos3);
    }

    public static Image CreateThumbnailImage(Image image)
    {
        return ImageHelpers.DownSampleImageIfLargerThan(image, AppInfo.MediaResolutionThumbnail,
            AppInfo.MediaResolutionThumbnail, KnownResamplers.Lanczos3);
    }

    public static string GetPngWebpPagePath(MediaFile file)
    {
        return MediaFileExtensions.GetViewPath(file.GlobalId.ToString(), MediaFileSize.FitPage, ".webp");
    }

    public static async Task UploadPngWebpPreviews(IMediaStorage storage, MediaFile file, Image page, Image thumbnail,
        MemoryStream buffer, CancellationToken cancellationToken)
    {
        var encoder = new WebpEncoder
        {
            FileFormat = file.AllowLossyWebpPreviews ? WebpFileFormatType.Lossy : WebpFileFormatType.Lossless,
            Quality = 90,
            Method = WebpEncodingMethod.BestQuality,
            TransparentColorMode = TransparentColorMode.Preserve,
        };

        await ImageHelpers.SaveImageToMemoryStream(thumbnail, buffer, encoder, cancellationToken);
        var thumbnailPath = MediaFileExtensions.GetStoragePath(file, MediaFileSize.Thumbnail);
        await storage.UploadFile(thumbnailPath, buffer, "image/webp", false, cancellationToken);

        // Publish page-fit last so its presence also identifies files with converted thumbnails.
        await ImageHelpers.SaveImageToMemoryStream(page, buffer, encoder, cancellationToken);
        await storage.UploadFile(GetPngWebpPagePath(file), buffer, "image/webp", false, cancellationToken);
    }
}
