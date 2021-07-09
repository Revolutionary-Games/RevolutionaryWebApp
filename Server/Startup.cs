namespace ThriveDevCenter.Server
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using AspNetCoreRateLimit;
    using AspNetCoreRateLimit.Redis;
    using Authorization;
    using Controllers;
    using Filters;
    using Hangfire;
    using Hangfire.PostgreSql;
    using Hubs;
    using Jobs;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.HttpOverrides;
    using Microsoft.AspNetCore.ResponseCompression;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Models;
    using Modulight.Modules.Hosting;
    using Services;
    using Shared.Converters;
    using StackExchange.Redis;
    using StardustDL.RazorComponents.Markdown;
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

        private string SharedStateRedisConnectionString => Configuration.GetConnectionString("RedisSharedState");

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddOptions();

            if (!string.IsNullOrEmpty(SharedStateRedisConnectionString))
            {
                var redis = ConnectionMultiplexer.Connect(SharedStateRedisConnectionString);

                services.AddSingleton<IConnectionMultiplexer>(redis);

                services.AddDataProtection()
                    .PersistKeysToStackExchangeRedis(redis, "ThriveDevCenterDataProtectionKeys");

                services.AddStackExchangeRedisCache(options =>
                {
                    options.ConfigurationOptions = ConfigurationOptions.Parse(SharedStateRedisConnectionString);

                    // Silent background retry
                    options.ConfigurationOptions.AbortOnConnectFail = false;

                    options.Configuration = SharedStateRedisConnectionString;

                    // This already works for channel prefix
                    options.InstanceName = "ThriveDevSharedCache";
                });
            }

            // Used for rate limit storage (when not using redis)
            services.AddMemoryCache();

            services.AddModules(moduleHostBuilder =>
            {
                // moduleHostBuilder.AddMarkdownModule();
                moduleHostBuilder.UseRazorComponentClientModules().AddMarkdownModule();
            });

            // TODO: message pack protocol
            if (!string.IsNullOrEmpty(SharedStateRedisConnectionString))
            {
                services.AddSignalR().AddStackExchangeRedis(SharedStateRedisConnectionString,
                    options => { options.Configuration.ChannelPrefix = "ThriveDevNotifications"; });
            }
            else
            {
                services.AddSignalR();
            }

            services.AddSingleton<IModelUpdateNotificationSender, ModelUpdateNotificationSender>();

            // Caching used for expensive API endpoints
            services.AddResponseCaching(options => { options.UseCaseSensitivePaths = true; });

            services.AddControllersWithViews().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new TimeSpanConverter());

                // This doesn't seem to do anything... So manual deserialize on client needs case insensitive mode on
                // options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });

            services.AddRazorPages();

            services.AddResponseCompression(opts =>
            {
                opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                    new[] { "application/octet-stream" });
            });

            // Rate limit
            services.Configure<IpRateLimitOptions>(Configuration.GetSection("IpRateLimiting"));
            services.Configure<IpRateLimitPolicies>(Configuration.GetSection("IpRateLimitPolicies"));

            // Rate limit service setup
            if (!string.IsNullOrEmpty(SharedStateRedisConnectionString) &&
                Convert.ToBoolean(Configuration["RateLimitStorageAllowRedis"]))
            {
                services.AddRedisRateLimiting();
            }
            else
            {
                services.AddInMemoryRateLimiting();
            }

            services.AddDbContext<ApplicationDbContext>(opts =>
                opts.UseNpgsql(Configuration.GetConnectionString("WebApiConnection")));

            services.AddDbContext<NotificationsEnabledDb>(opts =>
                opts.UseNpgsql(Configuration.GetConnectionString("WebApiConnection")));

            // services.AddIdentity<User, IdentityRole<long>>().AddEntityFrameworkStores<ApplicationDbContext>();

            // For now the same DB is used for jobs
            services.AddHangfire(opts =>
            {
                opts.UsePostgreSqlStorage(Configuration.GetConnectionString("WebApiConnection"),
                    new PostgreSqlStorageOptions()
                    {
                        QueuePollInterval = TimeSpan.FromSeconds(3)
                    });
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
            services.AddSingleton<StaticHomePageNotice>();
            services.AddSingleton<LfsDownloadUrls>();
            services.AddSingleton<GeneralRemoteDownloadUrls>();
            services.AddSingleton<ILocalTempFileLocks, LocalTempFileLocks>();

            services.AddScoped<IPatreonAPI, PatreonAPI>();
            services.AddScoped<LfsRemoteStorage>();
            services.AddScoped<GeneralRemoteStorage>();
            services.AddScoped<DevForumAPI>();
            services.AddScoped<CommunityForumAPI>();
            services.AddScoped<RemoteServerHandler>();
            services.AddScoped<IEC2Controller, EC2Controller>();
            services.AddScoped<ControlledServerSSHAccess>();
            services.AddScoped<GithubCommitStatusReporter>();
            services.AddScoped<DiscordNotifications>();

            services.AddScoped<CSRFCheckerMiddleware>();
            services.AddScoped<LFSAuthenticationMiddleware>();
            services.AddScoped<TokenOrCookieAuthenticationMiddleware>();
            services.AddScoped<CookieOnlyBasicAuthenticationMiddleware>();
            services.AddScoped<AccessCodeAuthenticationMiddleware>();
            services.AddAuthentication(options =>
            {
                options.DefaultForbidScheme = "forbidScheme";
                options.AddScheme<MyForbidHandler>("forbidScheme", "Handle Forbidden");
            });

            // Required by rate limiting
            services.AddHttpContextAccessor();

            // Make dynamic rate limit configuration available
            services.AddSingleton<IRateLimitConfiguration, CustomRateLimitConfiguration>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var logger = app.ApplicationServices.GetRequiredService<ILogger<Startup>>();

            if (env.IsDevelopment() && !string.IsNullOrEmpty(Configuration["IsActuallyTesting"]))
            {
                var isTesting = Convert.ToBoolean(Configuration["IsActuallyTesting"]);

                var deleteDb =
                    Convert.ToBoolean(Environment.GetEnvironmentVariable("RECREATE_DB_IN_TESTING") ?? "false");

                if (isTesting && deleteDb)
                {
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

            if (!string.IsNullOrEmpty(SharedStateRedisConnectionString))
                logger.LogInformation("Shared state redis is configured");

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });

            app.UseResponseCompression();

            app.UseIpRateLimiting();

            app.UseWebSockets(new WebSocketOptions()
            {
                KeepAliveInterval = TimeSpan.FromSeconds(60)
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseWebAssemblyDebugging();
            }
            else
            {
                app.UseExceptionHandler("/Error");

                // Configure CORS?

                // app.UseHsts();
            }

            app.UseBlazorFrameworkFiles();

            // Files are only served in development, in production reverse proxy needs to serve them
            if (env.IsDevelopment())
                app.UseStaticFiles();

            app.UseResponseCaching();

            app.UseWhen(
                context => context.Request.Path.StartsWithSegments("/api/v1/lfs"),
                appBuilder => { appBuilder.UseMiddleware<LFSAuthenticationMiddleware>(); });

            app.UseWhen(
                context => context.Request.Path.StartsWithSegments("/api/v1/devbuild"),
                appBuilder => { appBuilder.UseMiddleware<AccessCodeAuthenticationMiddleware>(); });

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
                SchedulePollingInterval = TimeSpan.FromSeconds(10),
            });

            SetupDefaultJobs(Configuration.GetSection("Tasks:CronJobs"));

            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/ciBuildConnection")
                {
                    // New scope is probably needed here to not share the global ApplicationServices scope
                    await BuildWebSocketHandler.HandleHttpConnection(context,
                        app.ApplicationServices.CreateScope().ServiceProvider);
                }
                else
                {
                    await next();
                }
            });

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapHub<NotificationsHub>("/notifications");

                // Fallback to 404 for non-existent API routes
                endpoints.Map("/api/{*anything}", context =>
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                });

                // Fix for accessing these with ".something" as an URL suffix
                endpoints.MapFallbackToPage("/files/{*param}", "/_Host");
                endpoints.MapFallbackToPage("/lfs/{*param}", "/_Host");

                // For other routes the client side app loads and then that displays the path not found
                endpoints.MapFallbackToPage("/_Host");
            });

            // Early load the registration status
            app.ApplicationServices.GetRequiredService<IRegistrationStatus>();

            // Early load the redirect verification to check that BaseUrl is set
            app.ApplicationServices.GetRequiredService<RedirectVerifier>();
        }

        private void SetupDefaultJobs(IConfigurationSection configurationSection)
        {
            AddJobHelper<SessionCleanupJob>(configurationSection["SessionCleanupJob"]);
            AddJobHelper<CheckAllSSOUsersJob>(configurationSection["CheckAllSSOUsers"]);
            AddJobHelper<RefreshPatronsJob>(configurationSection["RefreshPatrons"]);
            AddJobHelper<RefreshLFSProjectFileTreesJob>(configurationSection["RefreshLFSFileTrees"]);
            AddJobHelper<DetectStuckServersJob>(configurationSection["DetectStuckServers"]);
            AddJobHelper<DetectLeftOnServersJob>(configurationSection["DetectLeftOnServers"]);
            AddJobHelper<TerminateLongStoppedServersJob>(configurationSection["TerminateLongStoppedServers"]);
            AddJobHelper<ScheduleServerMaintenanceJob>(configurationSection["ScheduleServerMaintenance"]);

            BackgroundJob.Enqueue<CreateDefaultFoldersJob>(x => x.Execute(CancellationToken.None));

            // This is kept here if in the future more hashed fields are needed to be added so this might be needed
            // in the future as well to update info in the db
            // BackgroundJob.Enqueue<QueueRecomputeHashIfNeededJob>(x => x.Execute(CancellationToken.None));
        }

        private static void AddJobHelper<T>(string schedule) where T : class, IJob
        {
            RecurringJob.AddOrUpdate<T>(x => x.Execute(CancellationToken.None), schedule);
        }
    }
}
