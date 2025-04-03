namespace RevolutionaryWebApp.Server.Utilities;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

/// <summary>
///   Handles dynamic generation of various images
/// </summary>
public sealed class ImageGenerator : IDisposable
{
    private const int LetterAvatarSize = 256;

    private const int ProgressUpdateBannerWidth = 1200;
    private const int ProgressUpdateBannerHeight = 630;

    private const string JuraFontUrl = "https://dev.revolutionarygamesstudio.com/api/v1/download/179252";
    private const string ThriveFontUrl = "https://dev.revolutionarygamesstudio.com/api/v1/download/179255";

    private const string ProgressUpdateBackgroundUrl =
        "https://dev.revolutionarygamesstudio.com/api/v1/download/179256";

    private const string ProgressUpdateBanner2 = "https://dev.revolutionarygamesstudio.com/api/v1/download/179375";

    private readonly IHttpClientFactory httpClientFactory;

    private readonly SemaphoreSlim fontDownloadSemaphore = new(1, 1);

    private FontFamily? juraFont;
    private FontFamily? thriveFont;

    private Image<Rgb24>? progressUpdateBackground;
    private Image<Rgb24>? progressUpdateBackground2;

    public ImageGenerator(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
    }

    /// <summary>
    ///   Helper to generate initials (one or two letters)
    /// </summary>
    /// <param name="name">Name / username</param>
    /// <returns>Initials</returns>
    public static string GetInitials(string name)
    {
        var parts = name.Trim().Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "?";

        // Single name
        if (parts.Length == 1)
        {
            return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
        }

        // Use the first letter of the first two words
        return (parts[0][0] + parts[1][0].ToString()).ToUpper();
    }

    public async Task<byte[]> GenerateProgressUpdateBanner(DateTime date)
    {
        await GetMissingBannerFontsIfNeeded();

        var fontColour = Color.Black;
        int version = 1;

        if (date > new DateTime(2025, 03, 25))
        {
            version = 2;
            fontColour = Color.White;
        }

        var backgroundImage = await GetProgressUpdateBackgroundImage(version);

        using var image = new Image<Rgb24>(ProgressUpdateBannerWidth, ProgressUpdateBannerHeight, Color.Black);

        var font = juraFont!.Value.CreateFont(96, FontStyle.Regular);

        // Setting bold here does nothing
        // var font = juraFont!.Value.CreateFont(96, FontStyle.Bold);

        var titleFont = thriveFont!.Value.CreateFont(128, FontStyle.Regular);

        image.Mutate(ctx =>
        {
            // Draw the background
            ctx.DrawImage(backgroundImage, new Point(0, 0), 1.0f);

            // The progress update text
            ctx.DrawText(new RichTextOptions(font)
            {
                TextAlignment = TextAlignment.Center,
                Font = titleFont,
                Origin = new PointF(ProgressUpdateBannerWidth / 2.0f,
                    ProgressUpdateBannerHeight / 2.0f - ProgressUpdateBannerHeight / 6.0f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }, "PROGRESS\nUPDATE", fontColour);

            // And the current date
            var dateLocation = new PointF(ProgressUpdateBannerWidth / 2.0f,
                ProgressUpdateBannerHeight / 2.0f + ProgressUpdateBannerHeight / 6.0f);

            if (version > 1)
            {
                dateLocation = new PointF(ProgressUpdateBannerWidth / 2.0f,
                    ProgressUpdateBannerHeight / 2.0f + ProgressUpdateBannerHeight / 4.0f);
            }

            ctx.DrawText(new RichTextOptions(font)
            {
                TextAlignment = TextAlignment.Center,
                Font = font,
                Origin = dateLocation,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }, date.ToString("MMMM dd yyyy", CultureInfo.InvariantCulture), fontColour);
        });

        // Save the image after generating
        using var memoryStream = new MemoryStream();
        await image.SaveAsync(memoryStream, new WebpEncoder
        {
            FileFormat = WebpFileFormatType.Lossless,
            Quality = 95,
        });

        return memoryStream.ToArray();
    }

    public async Task<byte[]> GenerateLetterAvatar(string initials, Color backgroundColor)
    {
        // Use ImageSharp to generate the image
        using var image = new Image<Rgb24>(LetterAvatarSize, LetterAvatarSize, backgroundColor);

        // Draw initials in the center
        // TODO: customize the font (this should be present on most Linuxes)
        var font = SystemFonts.CreateFont("DejaVu Sans", 96, FontStyle.Bold);

        image.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(font)
            {
                TextAlignment = TextAlignment.Center,
                Font = font,
                Origin = new PointF(LetterAvatarSize / 2.0f, LetterAvatarSize / 2.0f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }, initials, Color.White);
        });

        // Save the image after generating
        using var memoryStream = new MemoryStream();
        await image.SaveAsync(memoryStream, new WebpEncoder
        {
            FileFormat = WebpFileFormatType.Lossless,
            Quality = 85,
        });

        return memoryStream.ToArray();
    }

    public void Dispose()
    {
        fontDownloadSemaphore.Dispose();
    }

    private async ValueTask GetMissingBannerFontsIfNeeded()
    {
        if (juraFont == null)
        {
            await fontDownloadSemaphore.WaitAsync();
            try
            {
                // In case it just got downloaded
                if (juraFont == null)
                {
                    var collection = await DownloadFont(JuraFontUrl);
                    juraFont = collection.Families.First();
                }
            }
            finally
            {
                fontDownloadSemaphore.Release();
            }
        }

        if (thriveFont == null)
        {
            await fontDownloadSemaphore.WaitAsync();
            try
            {
                if (thriveFont == null)
                {
                    var collection = await DownloadFont(ThriveFontUrl);
                    thriveFont = collection.Families.First();
                }
            }
            finally
            {
                fontDownloadSemaphore.Release();
            }
        }
    }

    private async ValueTask<Image<Rgb24>> GetProgressUpdateBackgroundImage(int version)
    {
        if (version < 2)
        {
            if (progressUpdateBackground == null)
            {
                await fontDownloadSemaphore.WaitAsync();
                try
                {
                    progressUpdateBackground ??= await DownloadImage(ProgressUpdateBackgroundUrl);
                }
                finally
                {
                    fontDownloadSemaphore.Release();
                }
            }

            return progressUpdateBackground;
        }

        if (progressUpdateBackground2 == null)
        {
            await fontDownloadSemaphore.WaitAsync();
            try
            {
                progressUpdateBackground2 ??= await DownloadImage(ProgressUpdateBanner2);
            }
            finally
            {
                fontDownloadSemaphore.Release();
            }
        }

        return progressUpdateBackground2;
    }

    private async Task<Image<Rgb24>> DownloadImage(string downloadUrl)
    {
        using var client = httpClientFactory.CreateClient();

        var response = await client.GetAsync(downloadUrl);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStreamAsync();

        return await Image.LoadAsync<Rgb24>(content);
    }

    private async Task<FontCollection> DownloadFont(string fontUrl)
    {
        using var client = httpClientFactory.CreateClient();

        var response = await client.GetAsync(fontUrl);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStreamAsync();

        var collection = new FontCollection();
        collection.Add(content);
        return collection;
    }
}
