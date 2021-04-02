namespace ThriveDevCenter.Server.Services
{
    using Microsoft.Extensions.Configuration;

    public class StaticHomePageNotice
    {
        private readonly string text;

        public StaticHomePageNotice(IConfiguration configuration)
        {
            text = configuration["StaticSiteHomePageNotice"];
        }

        public bool Enabled => !string.IsNullOrEmpty(text);

        public string Text => text;
    }
}
