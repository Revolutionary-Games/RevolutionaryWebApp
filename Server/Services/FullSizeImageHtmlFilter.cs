namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shared.Models.Pages;
using Shared.Services;
using Utilities;

internal class FullSizeImageHtmlFilter(IConfiguration configuration, IMediaLinkConverter linkConverter)
    : IPageHtmlFilter
{
    private static readonly Regex PageFitImagePath = new(@"/([a-f0-9-]{36})/page-fit(\.\w+)$",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    public bool WantsToProcess(string html, PageHtmlContext context)
    {
        return context.ImageDatabase != null && html.Contains("/page-fit.", StringComparison.Ordinal);
    }

    public async ValueTask<bool> Process(IHtmlElement body, PageHtmlContext context)
    {
        var images = new List<(IElement Image, Guid Id, string Extension)>();

        foreach (var image in body.QuerySelectorAll("img[src]:not([usemap])"))
        {
            if (image.Closest("a, button, label, summary, [role=button], [role=link], [onclick]") != null)
                continue;

            var source = image.GetAttribute("src")!;
            var match = PageFitImagePath.Match(source);
            if (!match.Success || !Guid.TryParse(match.Groups[1].Value, out var id))
                continue;

            var extension = match.Groups[2].Value;
            if (source != linkConverter.TranslateImageLink(extension, id.ToString(), MediaFileSize.FitPage))
                continue;

            images.Add((image, id, extension));
        }

        if (images.Count == 0)
            return false;

        var ids = images.Select(image => image.Id).Distinct().ToList();
        var files = await context.ImageDatabase!.MediaFiles.AsNoTracking()
            .Where(file => ids.Contains(file.GlobalId) && file.Processed && !file.Deleted)
            .Select(file => new { file.GlobalId, file.Id, file.Name, file.OriginalWidth, file.OriginalHeight })
            .ToDictionaryAsync(file => file.GlobalId);

        var serverBaseUrl = configuration.GetBaseUrl();
        foreach (var (image, id, extension) in images)
        {
            image.SetAttribute("data-large-src", linkConverter.TranslateImageLink(extension, id.ToString(),
                MediaFileSize.Large));

            if (files.TryGetValue(id, out var file))
            {
                var viewPath = $"api/v1/MediaFile/view/{file.Id.ToString(CultureInfo.InvariantCulture)}";
                var viewUrl = QueryHelpers.AddQueryString(new Uri(serverBaseUrl, viewPath).AbsoluteUri, "name",
                    file.Name);
                image.SetAttribute("data-original-src", viewUrl);
                if (file.OriginalWidth > 0 && file.OriginalHeight > 0)
                {
                    image.SetAttribute("data-original-width",
                        file.OriginalWidth.ToString(CultureInfo.InvariantCulture));
                    image.SetAttribute("data-original-height",
                        file.OriginalHeight.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        return true;
    }
}
