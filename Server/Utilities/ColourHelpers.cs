namespace RevolutionaryWebApp.Server.Utilities;

using System;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

public static class ColourHelpers
{
    /// <summary>
    ///   Generates a random colour based on a string for use as a background for white text
    /// </summary>
    /// <param name="seed">String to use as a seed value</param>
    /// <returns>Random colour</returns>
    public static Color GenerateBackgroundColor(string seed)
    {
        // Use a hash of the name as a deterministic seed
        using var md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));

        // Use the hash to derive HSL values
        // Hue: 0-360
        int hue = hashBytes[0] % 360;

        // Saturation: 50-100%
        int saturation = hashBytes[1] % 50 + 50;

        // Lightness: 30-70%
        int lightness = hashBytes[2] % 40 + 30;

        // Convert the result
        return FromHsl(hue, saturation / 100.0, lightness / 100.0);
    }

    public static bool HasSufficientContrast(Color background, Color text, double threshold = 4.5)
    {
        // Calculate relative luminance of the colours
        double backgroundLuminance = Luminance(FromImageSharp(background));
        double textLuminance = Luminance(FromImageSharp(text));

        // Calculate contrast ratio
        double contrastRatio = (Math.Max(backgroundLuminance, textLuminance) + 0.05) /
            (Math.Min(backgroundLuminance, textLuminance) + 0.05);

        return contrastRatio >= threshold;
    }

    public static double Luminance(System.Drawing.Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double r2 = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        double g2 = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        double b2 = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

        return 0.2126 * r2 + 0.7152 * g2 + 0.0722 * b2;
    }

    private static System.Drawing.Color FromImageSharp(Color color)
    {
        var pixel = color.ToPixel<Rgba32>();
        return System.Drawing.Color.FromArgb(pixel.A, pixel.R, pixel.G, pixel.B);
    }

    private static Color FromHsl(int h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        double m = l - c / 2;

        double r = 0, g = 0, b = 0;

        if (h is >= 0 and < 60)
        {
            r = c;
            g = x;
            b = 0;
        }
        else if (h is >= 60 and < 120)
        {
            r = x;
            g = c;
            b = 0;
        }
        else if (h is >= 120 and < 180)
        {
            r = 0;
            g = c;
            b = x;
        }
        else if (h is >= 180 and < 240)
        {
            r = 0;
            g = x;
            b = c;
        }
        else if (h is >= 240 and < 300)
        {
            r = x;
            g = 0;
            b = c;
        }
        else if (h is >= 300 and < 360)
        {
            r = c;
            g = 0;
            b = x;
        }

        // Apply the offset (m) to linearize RGB values into 0-255
        return Color.FromRgb((byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255));
    }
}
