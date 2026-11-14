namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Translations;
using Shared.Models;
using Shared.Models.Enums;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;

/// <summary>
///   Public, embeddable translation progress images.
/// </summary>
[APIKeyScope(APIKeyScope.Translations)]
[ApiController]
[Route("widgets")]
[Route("translations/widgets")]
public class TranslationWidgetsController : Controller
{
    private const int CacheSeconds = 1800;

    private const int BarWidth = 260;
    private const int RowHeight = 28;

    private const string ProgressFontResourceName =
        "RevolutionaryWebApp.Server.TranslationWidgets.Lato-Black.ttf";

    private static readonly Lazy<FontFamily> ProgressFont = new(LoadProgressFont);

    private readonly ApplicationDbContext database;

    public TranslationWidgetsController(ApplicationDbContext database)
    {
        this.database = database;
    }

    [HttpGet("{slug}/horizontal-auto.svg")]
    [OutputCache(Duration = CacheSeconds, VaryByQueryKeys = [nameof(slug)])]
    public async Task<IActionResult> Progress(string slug, CancellationToken cancellationToken)
    {
        var progress = await LoadProgressDataAsync(slug, cancellationToken);
        if (progress == null)
            return NotFound();

        Response.Headers.CacheControl = $"public, max-age={CacheSeconds}";
        return Content(GenerateImage(progress.Value.Project.Name, progress.Value.Languages),
            "image/svg+xml; charset=utf-8");
    }

    [HttpGet("{slug}/horizontal-auto.png")]
    [OutputCache(Duration = CacheSeconds, VaryByQueryKeys = [nameof(slug)])]
    public async Task<IActionResult> ProgressPng(string slug, CancellationToken cancellationToken)
    {
        var progress = await LoadProgressDataAsync(slug, cancellationToken);
        if (progress == null)
            return NotFound();

        Response.Headers.CacheControl = $"public, max-age={CacheSeconds}";
        return File(GeneratePngImage(progress.Value.Project.Name, progress.Value.Languages), "image/png");
    }

    [NonAction]
    private static string GenerateImage(string projectName, TranslationLanguageDTO[] languages)
    {
        var labels = languages.Select(language => $"{language.DisplayName} ({language.LanguageCode})").ToArray();
        var labelWidth = Math.Max(180, labels.Select(label => label.Length * 9).DefaultIfEmpty(0).Max());
        var width = Math.Max(labelWidth + BarWidth + 104, projectName.Length * 14 + 32);
        var height = 76 + Math.Max(1, languages.Length) * RowHeight;
        XNamespace svg = "http://www.w3.org/2000/svg";

        var image = new XElement(svg + "svg",
            new XAttribute("width", width), new XAttribute("height", height),
            new XAttribute("viewBox", $"0 0 {width} {height}"),
            new XAttribute("role", "img"), new XAttribute("aria-labelledby", "title"),
            new XAttribute("font-family", "DejaVu Sans, sans-serif"),
            new XAttribute("font-size", 14), new XAttribute("fill", "#243746"),
            new XElement(svg + "title", new XAttribute("id", "title"), $"{projectName} translation progress"),
            new XElement(svg + "rect", new XAttribute("width", width), new XAttribute("height", height),
                new XAttribute("fill", "#ffffff")),
            new XElement(svg + "text", new XAttribute("x", 16), new XAttribute("y", 30),
                new XAttribute("font-size", 22), new XAttribute("font-weight", "bold"), projectName),
            new XElement(svg + "text", new XAttribute("x", 16), new XAttribute("y", 54),
                "Translation completion"));

        for (int i = 0; i < languages.Length; ++i)
        {
            var language = languages[i];
            var percent = language.Total == 0 ? 0 : Math.Clamp(100.0 * language.Translated / language.Total, 0, 100);
            var y = 72 + i * RowHeight;
            image.Add(new XElement(svg + "text", new XAttribute("x", 16), new XAttribute("y", y + 15), labels[i]),
                new XElement(svg + "rect", new XAttribute("x", labelWidth + 32), new XAttribute("y", y),
                    new XAttribute("width", BarWidth), new XAttribute("height", 20),
                    new XAttribute("rx", 3), new XAttribute("fill", "#e5e9ed")),
                new XElement(svg + "rect", new XAttribute("x", labelWidth + 32), new XAttribute("y", y),
                    new XAttribute("width", BarWidth * percent / 100), new XAttribute("height", 20),
                    new XAttribute("rx", 3), new XAttribute("fill", "#2b9464")),
                new XElement(svg + "text", new XAttribute("x", width - 16), new XAttribute("y", y + 15),
                    new XAttribute("text-anchor", "end"), percent.ToString("0.#", CultureInfo.InvariantCulture) + "%"));
        }

        if (languages.Length == 0)
        {
            image.Add(new XElement(svg + "text", new XAttribute("x", 16), new XAttribute("y", 87),
                "No languages available"));
        }

        return image.ToString(SaveOptions.DisableFormatting);
    }

    [NonAction]
    private static byte[] GeneratePngImage(string projectName, TranslationLanguageDTO[] languages)
    {
        var fontFamily = ProgressFont.Value;
        var labelFont = fontFamily.CreateFont(14, FontStyle.Regular);
        var titleFont = fontFamily.CreateFont(22, FontStyle.Bold);
        var subtitleFont = fontFamily.CreateFont(14, FontStyle.Regular);
        var labels = languages.Select(language => $"{language.DisplayName} ({language.LanguageCode})").ToArray();
        var labelWidth = Math.Max(180, labels.Select(label => TextMeasurer
            .MeasureAdvance(label, new TextOptions(labelFont))
            .Width).DefaultIfEmpty(0).Max());
        var projectNameWidth = TextMeasurer.MeasureAdvance(projectName, new TextOptions(titleFont)).Width;
        var width = (int)Math.Ceiling(Math.Max(labelWidth + BarWidth + 104, projectNameWidth + 32));
        var height = 76 + Math.Max(1, languages.Length) * RowHeight;
        var textColor = Color.FromPixel(new Rgb24(36, 55, 70));
        var emptyBarColor = Color.FromPixel(new Rgb24(229, 233, 237));
        var progressColor = Color.FromPixel(new Rgb24(43, 148, 100));

        using var image = new Image<Rgba32>(width, height);
        image.Mutate(context =>
        {
            context.Paint(canvas =>
            {
                canvas.Fill(Brushes.Solid(Color.White), new RectanglePolygon(0, 0, width, height));
                canvas.DrawText(new RichTextOptions(titleFont) { Origin = new PointF(16, 8) }, projectName,
                    Brushes.Solid(textColor), null);
                canvas.DrawText(new RichTextOptions(subtitleFont) { Origin = new PointF(16, 38) },
                    "Translation completion", Brushes.Solid(textColor), null);

                for (int i = 0; i < languages.Length; ++i)
                {
                    var language = languages[i];
                    var percent = language.Total == 0 ?
                        0 :
                        Math.Clamp(100.0 * language.Translated / language.Total, 0, 100);
                    var y = 72 + i * RowHeight;
                    var barX = labelWidth + 32;
                    var barY = y + 1;

                    canvas.DrawText(new RichTextOptions(labelFont) { Origin = new PointF(16, y + 3) }, labels[i],
                        Brushes.Solid(textColor), null);
                    canvas.Fill(Brushes.Solid(emptyBarColor), new RectanglePolygon(barX, barY, BarWidth, 20));
                    if (percent > 0)
                    {
                        canvas.Fill(Brushes.Solid(progressColor),
                            new RectanglePolygon(barX, barY, BarWidth * (float)percent / 100, 20));
                    }

                    var percentText = percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
                    var percentWidth = TextMeasurer.MeasureAdvance(percentText, new TextOptions(labelFont)).Width;
                    canvas.DrawText(new RichTextOptions(labelFont)
                    {
                        Origin = new PointF(width - 16 - percentWidth, y + 3),
                    }, percentText, Brushes.Solid(textColor), null);
                }

                if (languages.Length == 0)
                {
                    canvas.DrawText(new RichTextOptions(labelFont) { Origin = new PointF(16, 80) },
                        "No languages available", Brushes.Solid(textColor), null);
                }
            });
        });

        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }

    [NonAction]
    private static FontFamily LoadProgressFont()
    {
        using var fontStream = typeof(TranslationWidgetsController).Assembly
                .GetManifestResourceStream(ProgressFontResourceName) ??
            throw new InvalidOperationException("Embedded translation widget font resource is missing");
        var fontCollection = new FontCollection();
        return fontCollection.Add(fontStream);
    }

    [NonAction]
    private async Task<(TranslationProject Project, TranslationLanguageDTO[] Languages)?> LoadProgressDataAsync(
        string slug, CancellationToken cancellationToken)
    {
        var project = await database.TranslationProjects.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Slug == slug && item.Enabled, cancellationToken);
        if (project == null)
            return null;

        var total = await database.TranslationUnits.CountAsync(unit => unit.ProjectId == project.Id && unit.Active,
            cancellationToken);

        var languages = await database.TranslationProjectLanguages.AsNoTracking()
            .Where(language => language.ProjectId == project.Id && language.Enabled)
            .OrderBy(language => language.DisplayName).ThenBy(language => language.LanguageCode)
            .Select(language => new TranslationLanguageDTO
            {
                DisplayName = language.DisplayName,
                LanguageCode = language.LanguageCode,
                Total = total,
                Translated = database.TranslationValues.Count(value => value.ProjectId == project.Id &&
                    value.LanguageCode == language.LanguageCode && value.Unit.Active &&
                    (value.State == TranslationState.Translated || value.State == TranslationState.Verified)),
            }).ToArrayAsync(cancellationToken);

        return (project, languages);
    }
}
