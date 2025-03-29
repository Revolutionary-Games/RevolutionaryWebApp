namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.Collections.Generic;
using Models.Pages;

public class RenderedPage
{
    public RenderedPage(string title, string rendered, DateTime versionUpdateTime, TimeSpan renderTime)
    {
        Title = title;
        RenderedHtml = rendered;
        UpdatedAt = versionUpdateTime;
        RenderTime = renderTime;
    }

    public bool ShowHeading { get; set; }

    public bool ShowLogo { get; set; }

    public string Title { get; set; }

    public string RenderedHtml { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string ByServer { get; set; } = "server";
    public TimeSpan RenderTime { get; set; }

    public DateTime RenderedAt { get; set; } = DateTime.UtcNow;

    public string? CanonicalUrl { get; set; }

    // Navigation and sidebar
    public List<RenderingLayoutPart>? TopNavigation { get; set; }

    public List<RenderingLayoutPart>? Sidebar { get; set; }

    public List<RenderingLayoutPart>? Socials { get; set; }

    // Opengraph info
    public string? OpenGraphMetaDescription { get; set; }
    public string? PreviewImage { get; set; }

    public string OpenGraphPageType { get; set; } = "article";
}
