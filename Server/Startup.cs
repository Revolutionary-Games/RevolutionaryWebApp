namespace ThriveDevCenter.Server
{
    using System;
    using System.Linq;
    using System.Threading;
    using Authorization;
    using Filters;
    using Hangfire;
    using Hangfire.PostgreSql;
    using Hubs;
    using Jobs;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.HttpOverrides;
    using Microsoft.AspNetCore.ResponseCompression;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Utilities;

    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            var extra = Environment.GetEnvironmentVariable("HACK_LOAD_EXTRA_ENVIRONMENT");

            if (extra != null)
            {
                var builder = new ConfigurationBuilder().AddConfiguration(configuration).AddJsonFile(extra);

                Configuration = builder.Build();
            }
            else
            {
                Configuration = configuration;
            }
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

            // For now the same DB is used for jobs
            services.AddHangfire(opts =>
            {
                opts.UsePostgreSqlStorage(Configuration.GetConnectionString("WebApiConnection"));
                opts.SetDataCompatibilityLevel(CompatibilityLevel.Version_170);
                opts.UseSimpleAssemblyNameTypeSerializer();
                opts.UseDefaultTypeSerializer();
            });

            services.AddControllers(options =>
            {
                options.ModelMetadataDetailsProviders.Add(new RequiredBindingMetadataProvider());
                options.Filters.Add(new HttpResponseExceptionFilter());
            });

            services.AddSingleton<IRegistrationStatus, RegistrationStatus>();
            services.AddSingleton<ITokenGenerator, TokenGenerator>();
            services.AddSingleton<ITokenVerifier, TokenVerifier>();
            services.AddSingleton<RedirectVerifier>();

            services.AddScoped<IPatreonAPI, PatreonAPI>();

            services.AddScoped<CSRFCheckerMiddleware>();
            services.AddScoped<LFSAuthenticationMiddleware>();
            services.AddScoped<TokenOrCookieAuthenticationMiddleware>();
            services.AddScoped<CookieOnlyBasicAuthenticationMiddleware>();
            services.AddAuthentication(options =>
            {
                options.DefaultForbidScheme = "forbidScheme";
                options.AddScheme<MyForbidHandler>("forbidScheme", "Handle Forbidden");
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment() && !string.IsNullOrEmpty(Configuration["IsActuallyTesting"]))
            {
                var isTesting = Convert.ToBoolean(Configuration["IsActuallyTesting"]);

                var deleteDb =
                    Convert.ToBoolean(Environment.GetEnvironmentVariable("RECREATE_DB_IN_TESTING") ?? "false");

                if (isTesting && deleteDb)
                {
                    var logger = app.ApplicationServices.GetRequiredService<ILogger<Startup>>();
                    logger.LogInformation("Recreating DB because in Testing environment with that enabled");

                    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                        .UseNpgsql(Configuration.GetConnectionString("WebApiConnection"))
                        .Options;

                    var db = new ApplicationDbContext(options);
                    db.Database.EnsureDeleted();
                    db.Database.EnsureCreated();

                    // TODO: seed some special data
                }
            }

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });

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
                context => context.Request.Path.StartsWithSegments("/hangfire"),
                appBuilder => { appBuilder.UseMiddleware<CookieOnlyBasicAuthenticationMiddleware>(); });

            app.UseWhen(
                context => context.Request.Path.StartsWithSegments("/api"),
                appBuilder => { appBuilder.UseMiddleware<TokenOrCookieAuthenticationMiddleware>(); });

            app.UseWhen(
                context => context.Request.Path.StartsWithSegments("/api"),
                appBuilder => { appBuilder.UseMiddleware<CSRFCheckerMiddleware>(); });

            app.UseHangfireDashboard("/hangfire", new DashboardOptions()
            {
                Authorization = new[]
                {
                    new HangfireDashboardAuthorization(
                        app.ApplicationServices.GetRequiredService<ILogger<HangfireDashboardAuthorization>>())
                }
            });

            app.UseHangfireServer(new BackgroundJobServerOptions()
            {
                WorkerCount = Convert.ToInt32(Configuration["Tasks:ThreadCount"]),
                SchedulePollingInterval = TimeSpan.FromSeconds(3),
            });

            SetupDefaultJobs(Configuration.GetSection("Tasks:CronJobs"));

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapHub<NotificationsHub>("/notifications");
                endpoints.MapFallbackToPage("/_Host");
            });

            // Early load the registration status
            app.ApplicationServices.GetRequiredService<IRegistrationStatus>();

            // Early load the redirect verification to check that BaseUrl is set
            app.ApplicationServices.GetRequiredService<RedirectVerifier>();
        }

        private void SetupDefaultJobs(IConfigurationSection configurationSection)
        {
            AddJobHelper<SessionCleanupJob>(configurationSection);
        }

        private static void AddJobHelper<T>(IConfigurationSection configuration) where T : class, IJob
        {
            var name = typeof(T).Name;

            RecurringJob.AddOrUpdate<T>(x => x.Execute(CancellationToken.None), configuration["SessionCleanupJob"]);
        }
    }
}
