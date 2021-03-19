namespace ThriveDevCenter.Server
{
    using System.Linq;
    using Authorization;
    using Hubs;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.ResponseCompression;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Models;

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

            services.AddDbContext<ApplicationDbContext>(opts =>
                opts.UseNpgsql(Configuration.GetConnectionString("WebApiConnection")));

            // services.AddIdentity<User, IdentityRole<long>>().AddEntityFrameworkStores<ApplicationDbContext>();

            services.AddControllers(options =>
            {
                options.ModelMetadataDetailsProviders.Add(new RequiredBindingMetadataProvider());
                options.Filters.Add(new HttpResponseExceptionFilter());
            });

            services.AddSingleton<RegistrationStatus>();
            services.AddSingleton<JwtTokens>();

            services.AddScoped<CSRFCheckerMiddleware>();
            services.AddScoped<LFSAuthenticationMiddleware>();
            services.AddScoped<TokenOrCookieAuthenticationMiddleware>();
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

            // Files are only served in development, in production reverse proxy needs to serve them
            if (env.IsDevelopment())
                app.UseStaticFiles();

            app.UseWhen(
                context => context.Request.Path.StartsWithSegments("/api/v1/lfs"),
                appBuilder => { appBuilder.UseMiddleware<LFSAuthenticationMiddleware>(); });

            app.UseWhen(
                context => context.Request.Path.StartsWithSegments("/api"),
                appBuilder => { appBuilder.UseMiddleware<TokenOrCookieAuthenticationMiddleware>(); });

            app.UseWhen(
                context => context.Request.Path.StartsWithSegments("/api"),
                appBuilder => { appBuilder.UseMiddleware<CSRFCheckerMiddleware>(); });

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapHub<NotificationsHub>("/notifications");
                endpoints.MapFallbackToPage("/_Host");
            });

            // Early load the registration status
            app.ApplicationServices.GetRequiredService<RegistrationStatus>();
        }
    }
}
