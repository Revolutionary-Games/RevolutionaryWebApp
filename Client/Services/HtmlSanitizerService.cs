namespace ThriveDevCenter.Client.Services;

using Ganss.Xss;

public class HtmlSanitizerService
{
    private readonly HtmlSanitizer sanitizer = new();

    public HtmlSanitizerService()
    {
        // sanitizer.AllowedTags.Add("math");
    }

    public string SanitizeHtml(string html)
    {
        return sanitizer.Sanitize(html);
    }
}
