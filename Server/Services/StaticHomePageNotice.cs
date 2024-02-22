namespace RevolutionaryWebApp.Server.Services;

using Microsoft.Extensions.Configuration;

public class StaticHomePageNotice
{
    private readonly string text;

    public StaticHomePageNotice(IConfiguration configuration)
    {
        text = configuration["StaticSiteHomePageNotice"] ?? string.Empty;

#if DEBUG
        if (!string.IsNullOrEmpty(text))
            text += " ";

        text += " [SERVER IN DEBUG MODE]";
#endif
    }

    public bool Enabled => !string.IsNullOrEmpty(text);

    public string Text => text;
}
