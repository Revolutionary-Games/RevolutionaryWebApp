namespace ThriveDevCenter.Client
{
    using System;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Blazored.LocalStorage;
    using Blazored.Modal;
    using BlazorPro.BlazorSize;
    using Microsoft.AspNetCore.Components;
    using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.JSInterop;
    using Modulight.Modules.Hosting;
    using Services;
    using ThriveDevCenter.Shared;
    using StardustDL.RazorComponents.Markdown;
    using TextCopy;

    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);

            // Not used when pre-rendering
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (!AppInfo.UsePrerendering)
                builder.RootComponents.Add<App>("#app");

            builder.Services.AddModules(moduleHostBuilder =>
            {
                moduleHostBuilder.UseRazorComponentClientModules().AddMarkdownModule();
            });

            builder.Services.AddSingleton(_ => new CurrentUserInfo());

            builder.Services.AddSingleton<ICSRFTokenReader>(sp =>
                new CSRFTokenReader(sp.GetRequiredService<IJSRuntime>(), sp.GetRequiredService<CurrentUserInfo>()));

            builder.Services.AddScoped(sp => new HttpClient
            {
                BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
                DefaultRequestHeaders = { { "X-CSRF-Token", sp.GetRequiredService<ICSRFTokenReader>().Token } }
            }).AddTransient<HttpCookieHandler>();

            builder.Services.AddScoped(sp =>
                new UsernameRetriever(sp.GetRequiredService<CurrentUserInfo>(), sp.GetRequiredService<HttpClient>()));

            builder.Services.AddBlazoredLocalStorage();
            builder.Services.AddMediaQueryService();

            builder.Services.AddScoped(sp => new ComponentUrlHelper(
                sp.GetRequiredService<IJSRuntime>(),
                sp.GetRequiredService<NavigationManager>()));

            builder.Services.AddSingleton(sp =>
                new NotificationHandler(sp.GetRequiredService<NavigationManager>(),
                    sp.GetRequiredService<CurrentUserInfo>(), sp.GetRequiredService<ICSRFTokenReader>()));

            builder.Services.AddSingleton<StaticHomePageNotice>();

            builder.Services.AddBlazoredModal();
            builder.Services.InjectClipboard();

            var app = builder.Build();

            // CSRF token is already needed here
            var tokenReader = app.Services.GetRequiredService<ICSRFTokenReader>();

            // This is probably a bit non-kosher, but this is the only place where we need to trigger these methods
            var concreteReader = (CSRFTokenReader)tokenReader;
            await concreteReader.Read();

            // Setup hub connection as soon as we are able
            // If this is awaited (especially in production) it slows down the app startup time, so that isn't done.
            // This in turn requires some careful programming before the connection has properly been established
#pragma warning disable 4014 // see comment above
            app.Services.GetRequiredService<NotificationHandler>().StartConnection();
#pragma warning restore 4014

            // This isn't really needed to happen instantly, so maybe this could not be waited for here if this
            // increases the app load time at all
            await concreteReader.ReportInitialUserIdToLocalStorage(app.Services
                .GetRequiredService<ILocalStorageService>());

            await app.RunAsyncWithModules();
        }
    }
}
