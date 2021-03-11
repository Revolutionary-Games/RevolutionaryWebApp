namespace ThriveDevCenter.Client
{
    using System;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Components;
    using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.JSInterop;
    using Shared;
    using ThriveDevCenter.Shared;

    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);

            // Not used when pre-rendering
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (!AppInfo.UsePrerendering)
                builder.RootComponents.Add<App>("#app");

            builder.Services.AddSingleton(sp =>
                new CSRFTokenReader(sp.GetRequiredService<IJSRuntime>()));

            builder.Services.AddScoped(sp => new HttpClient
            {
                BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
                DefaultRequestHeaders = { { "X-CSRF-Token", sp.GetRequiredService<CSRFTokenReader>().Token } }
            });

            builder.Services.AddScoped(sp => new ComponentUrlHelper(
                sp.GetRequiredService<IJSRuntime>(),
                sp.GetRequiredService<NavigationManager>()));

            builder.Services.AddSingleton(sp => new CurrentUserInfo());
            builder.Services.AddSingleton(sp =>
                new NotificationHandler(sp.GetRequiredService<NavigationManager>(),
                    sp.GetRequiredService<CurrentUserInfo>(), sp.GetRequiredService<CSRFTokenReader>()));

            var app = builder.Build();

            // CSRF token is already needed here
            await app.Services.GetRequiredService<CSRFTokenReader>().Read();

            // Setup hub connection as soon as we are able
            // Not awaiting this here doesn't seem to speed up things and requires some special careful programming,
            // so that is not done
            await app.Services.GetRequiredService<NotificationHandler>().StartConnection();

            await app.RunAsync();
        }
    }
}
