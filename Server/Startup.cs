namespace ThriveDevCenter.Server
{
    using System;
    using System.Linq;
    using System.Net.Http;
    using Hubs;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Hosting.Server;
    using Microsoft.AspNetCore.Hosting.Server.Features;
    using Microsoft.AspNetCore.ResponseCompression;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Shared;

    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            // TODO: message pack protocol
            services.AddSignalR();
            services.AddControllersWithViews();
            services.AddRazorPages();

            services.AddResponseCompression(opts =>
            {
                opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                    new[] { "application/octet-stream" });
            });

            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
#pragma warning disable 0162 // unreachable code
            if (AppVersion.UsePrerendering)
            {
                // Register service equivalents when pre-rendering on the server

                // Register a HttpClient that points to itself. From:
                // https://andrewlock.net/enabling-prerendering-for-blazor-webassembly-apps/ but it says there that
                // this isn't a very smart solution
                services.AddSingleton<HttpClient>(sp =>
                {
                    // Get the address that the app is currently running at
                    var server = sp.GetRequiredService<IServer>();
                    var addressFeature = server.Features.Get<IServerAddressesFeature>();
                    var baseAddress = addressFeature.Addresses.First();
                    return new HttpClient { BaseAddress = new Uri(baseAddress) };
                });
            }
#pragma warning restore 0162
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseResponseCompression();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseWebAssemblyDebugging();
            }
            else
            {
                app.UseExceptionHandler("/Error");

                // app.UseHsts();
            }

            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapHub<NotificationsHub>("/notifications");
                endpoints.MapFallbackToPage("/_Host");
            });
        }
    }
}
