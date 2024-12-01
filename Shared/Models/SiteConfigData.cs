namespace RevolutionaryWebApp.Shared.Models;

public class SiteConfigData(string mediaBaseUrl)
{
    public string MediaBaseUrl { get; set; } = mediaBaseUrl;
}
