namespace ThriveDevCenter.Client
{
    using System;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Blazored.LocalStorage;
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
            }).AddTransient<HttpCookieHandler>();

            builder.Services.AddBlazoredLocalStorage();

            builder.Services.AddScoped(sp => new ComponentUrlHelper(
                sp.GetRequiredService<IJSRuntime>(),
                sp.GetRequiredService<NavigationManager>()));

            builder.Services.AddSingleton(sp => new CurrentUserInfo());
            builder.Services.AddSingleton(sp =>
                new NotificationHandler(sp.GetRequiredService<NavigationManager>(),
                    sp.GetRequiredService<CurrentUserInfo>(), sp.GetRequiredService<CSRFTokenReader>()));

            var app = builder.Build();

            // CSRF token is already needed here
            var tokenReader = app.Services.GetRequiredService<CSRFTokenReader>();
            await tokenReader.Read();

            // Setup hub connection as soon as we are able
            // Not awaiting this here doesn't seem to speed up things and requires some special careful programming,
            // so that is not done
            await app.Services.GetRequiredService<NotificationHandler>().StartConnection();

            // This isn't really needed to happen instantly, so maybe this could not be waited for here if this
            // increases the app load time at all
            await tokenReader.ReportInitialUserIdToLocalStorage(app.Services.GetRequiredService<ILocalStorageService>());

            await app.RunAsync();
        }
    }
}
