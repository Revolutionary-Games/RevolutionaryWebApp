namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;

public class RenderedPage
{
    public RenderedPage(string title, string rendered, DateTime versionUpdateTime, TimeSpan renderTime)
    {
        Title = title;
        RenderedHtml = rendered;
        UpdatedAt = versionUpdateTime;
        RenderTime = renderTime;
    }

    /// <summary>
    ///   False when no page and view should give 404 special handling.
    /// </summary>
    public bool Found { get; set; }

    public bool ShowHeading { get; set; }

    public bool ShowLogo { get; set; }

    public string Title { get; set; }

    public string RenderedHtml { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string ByServer { get; set; } = "server";
    public TimeSpan RenderTime { get; set; }

    public DateTime RenderedAt { get; set; } = DateTime.UtcNow;

    public string? CanonicalUrl { get; set; }
}
