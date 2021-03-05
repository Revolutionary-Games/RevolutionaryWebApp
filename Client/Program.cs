using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace ThriveDevCenter.Client
{
    using Microsoft.AspNetCore.Components;
    using Shared;
    using ThriveDevCenter.Shared;

    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);

            // Not used when pre-rendering
            if(!AppVersion.UsePrerendering)
                builder.RootComponents.Add<App>("#app");

            builder.Services.AddScoped(sp => new HttpClient
                { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
            builder.Services.AddScoped(sp => new ComponentUrlHelper(
                sp.GetRequiredService<IJSRuntime>(),
                sp.GetRequiredService<NavigationManager>()));
            builder.Services.AddSingleton(sp =>
                new NotificationHandler(sp.GetRequiredService<NavigationManager>()));

            var app = builder.Build();

            // Setup hub connection as soon as we are able
            // Not awaiting this here doesn't seem to speed up things and requires some special careful programming,
            // so that is not done
            await app.Services.GetRequiredService<NotificationHandler>().StartConnection();

            await app.RunAsync();
        }
    }
}
