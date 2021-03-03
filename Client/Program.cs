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

    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
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
            // TODO: should this wait here? If not waited here we need a separate system that queues group
            // subscriptions before this is ready
            await app.Services.GetRequiredService<NotificationHandler>().StartConnection();

            await app.RunAsync();
        }
    }
}
