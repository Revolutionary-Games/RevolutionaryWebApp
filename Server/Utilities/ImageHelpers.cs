namespace RevolutionaryWebApp.Server.Utilities;

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

public static class ImageHelpers
{
    /// <summary>
    ///   Down samples an image to a smaller size if the size is currently bigger than wanted. Width is checked first.
    ///   Height is only checked against the target if width was small enough already.
    /// </summary>
    /// <param name="image">Image to make a smaller copy of (no copy is made if</param>
    /// <param name="targetWidth">Target width for the smaller image</param>
    /// <param name="targetHeight">Target height to reduce height if the image was narrow enough already</param>
    /// <param name="reSampler">Resampling algorithm to use</param>
    /// <returns>A scaled-down image (or original if it was small enough already)</returns>
    public static Image DownSampleImageIfLargerThan(Image image, int targetWidth, int targetHeight,
        IResampler? reSampler = null)
    {
        reSampler ??= default(LanczosResampler);
        // reSampler ??= default(BicubicResampler);

        // If small enough, don't need to do anything
        if (image.Width <= targetWidth && image.Height <= targetHeight)
            return image;

        if (image.Width > targetWidth)
        {
            return ResizeImage(image, targetWidth, 0, reSampler);
        }

        // Resize in another dimension if the aspect ratio was such as the first one wasn't too large
        if (image.Height > targetHeight)
        {
            return ResizeImage(image, 0, targetHeight, reSampler);
        }

        // If the image is small, pass the original through for efficiency
        return image;
    }

    public static Image ResizeImage(Image image, int width, int height, IResampler reSampler)
    {
        return image.Clone(i => i.Resize(new ResizeOptions
        {
            Sampler = reSampler,
            Size = new Size(width, height),
        }));
    }

    public static async Task SaveImageToMemoryStream(Image image, MemoryStream stream, ImageEncoder encoder,
        CancellationToken cancellationToken)
    {
        // Rewind the stream
        stream.Position = 0;

        await image.SaveAsync(stream, encoder, cancellationToken: cancellationToken);

        // Rewind the stream again for whatever code wants to read it next
        stream.Position = 0;
    }
}
