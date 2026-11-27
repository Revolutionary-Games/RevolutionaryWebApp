namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Html.Dom;
using Markdig.Helpers;

internal class HeadingAnchorHtmlFilter : IPageHtmlFilter
{
    public bool WantsToProcess(string html, PageHtmlContext context)
    {
        return context.RenderHeadingAnchors;
    }

    public ValueTask<bool> Process(IHtmlElement body, PageHtmlContext context)
    {
        var headings = body.QuerySelectorAll("h1, h2, h3, h4");
        if (headings.Length == 0)
            return ValueTask.FromResult(false);

        var usedIds = body.QuerySelectorAll("[id], a[name]")
            .SelectMany(element => new[] { element.Id, element.GetAttribute("name") })
            .ToHashSet(StringComparer.Ordinal);

        var document = body.Owner!;
        bool changed = false;
        foreach (var heading in headings)
        {
            if (heading.Closest("a, button") != null)
                continue;

            var title = heading.TextContent.Trim();
            var id = heading.Id;
            if (string.IsNullOrEmpty(id))
            {
                // Keep generated IDs separate from the public page layout's IDs.
                var slug = LinkHelper.UrilizeAsGfm(title.Normalize());
                var baseId = string.IsNullOrEmpty(slug) ? "section" : $"section-{slug}";
                id = baseId;

                for (int suffix = 2; !usedIds.Add(id); ++suffix)
                    id = $"{baseId}-{suffix.ToString(CultureInfo.InvariantCulture)}";

                heading.Id = id;
            }

            var link = document.CreateElement("a");
            link.ClassName = "heading-permalink";
            link.SetAttribute("href", "#" + Uri.EscapeDataString(id));
            link.SetAttribute("aria-label", title.Length > 0 ? $"Link to {title}" : "Link to this section");

            var icon = document.CreateElement("span");
            icon.ClassName = "fas fa-link";
            icon.SetAttribute("aria-hidden", "true");
            link.AppendChild(icon);
            heading.AppendChild(link);
            changed = true;
        }

        return ValueTask.FromResult(changed);
    }
}
