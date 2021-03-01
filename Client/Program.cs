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
            builder.Services.AddScoped(sp => new ComponentUrlHelper((IJSRuntime)sp.GetService(typeof(IJSRuntime)),
                (NavigationManager)sp.GetService(typeof(NavigationManager))));

            await builder.Build().RunAsync();
        }
    }
}
