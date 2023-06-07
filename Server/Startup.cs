namespace ThriveDevCenter.Server;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using Controllers;
using Filters;
using Formatters;
using Hangfire;
using Hangfire.PostgreSql;
using Hubs;
using Jobs;
using Jobs.RegularlyScheduled;
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
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Models;
using Modulight.Modules.Hosting;
using Services;
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

    private string SharedStateRedisConnectionString =>
        Configuration.GetConnectionString("RedisSharedState") ?? string.Empty;

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

        services.AddMemoryCache();

        // Our custom cache where we limit the total size, used only by our controllers we can make use this cache
        // properly
        services.AddSingleton<CustomMemoryCache>();

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

        services.AddControllersWithViews(options => { options.OutputFormatters.Add(new HtmlTextFormatter()); })
            .AddJsonOptions(_ =>
            {
                // Custom serializers for now also need to be configured in NotificationHelpers.ReceiveNotification
                // As well as in the Client project

                // This doesn't seem to do anything... So manual deserialize on client needs case insensitive mode on
                // options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });

        services.AddRazorPages();

        services.AddResponseCompression(opts =>
        {
            opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                new[] { "application/octet-stream" });
        });

        // Built in aspnet rate limit configuration
        var limitOptions = new MyRateLimitOptions();
        Configuration.GetSection("RateLimits").Bind(limitOptions);

        services.AddRateLimiter(limiterOptions =>
        {
            limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiterOptions.OnRejected = CustomRateLimiter.OnRejected;

            limiterOptions.GlobalLimiter = CustomRateLimiter.CreateGlobalLimiter(limitOptions);

            CustomRateLimiter.CreateLoginLimiter(limiterOptions, limitOptions);
        });

        services.AddDbContextPool<ApplicationDbContext>(opts =>
        {
            opts.UseSnakeCaseNamingConvention();
            opts.UseNpgsql(Configuration.GetConnectionString("WebApiConnection"));

#if DEBUG
            opts.EnableSensitiveDataLogging();
#endif
        });

        // Can't currently be pooled due to the IModelUpdateNotificationSender that is a second constructor
        // parameter
        services.AddDbContext<NotificationsEnabledDb>(opts =>
        {
            opts.UseSnakeCaseNamingConvention();
            opts.UseNpgsql(Configuration.GetConnectionString("WebApiConnection"));

#if DEBUG
            opts.EnableSensitiveDataLogging();
#endif
        });

        // services.AddIdentity<User, IdentityRole<long>>().AddEntityFrameworkStores<ApplicationDbContext>();

        // For now the same DB is used for jobs
        services.AddHangfire(opts =>
        {
            opts.UsePostgreSqlStorage(Configuration.GetConnectionString("WebApiConnection"),
                new PostgreSqlStorageOptions
                {
                    QueuePollInterval = TimeSpan.FromSeconds(3),
                });
            opts.SetDataCompatibilityLevel(CompatibilityLevel.Version_170);
            opts.UseSimpleAssemblyNameTypeSerializer();
            opts.UseDefaultTypeSerializer();
        });

        services.AddHangfireServer(opts =>
        {
            opts.WorkerCount = Convert.ToInt32(Configuration["Tasks:ThreadCount"]);
            opts.SchedulePollingInterval = TimeSpan.FromSeconds(10);
        });

        services.AddControllers(options =>
        {
            options.ModelMetadataDetailsProviders.Add(new RequiredBindingMetadataProvider());
            options.Filters.Add(new HttpResponseExceptionFilter());
        });

        services.AddHttpClient();
        services.AddHttpClient(Options.DefaultName, httpClient => { httpClient.AddDevCenterUserAgent(); });
        services.AddHttpClient("github", httpClient =>
        {
            httpClient.BaseAddress = new Uri("https://api.github.com/");
            httpClient.DefaultRequestHeaders.Add(
                HeaderNames.Accept, "application/vnd.github.v3+json");
            httpClient.AddDevCenterUserAgent();
        });
        services.AddHttpClient("stackwalk", httpClient =>
        {
            httpClient.Timeout = TimeSpan.FromSeconds(120);
            httpClient.AddDevCenterUserAgent();
        });
        services.AddHttpClient("discourse", httpClient =>
        {
            httpClient.Timeout = TimeSpan.FromSeconds(80);
            httpClient.AddDevCenterUserAgent();
        });

        services.AddSingleton<IRegistrationStatus, RegistrationStatus>();
        services.AddSingleton<ITokenGenerator, TokenGenerator>();
        services.AddSingleton<ITokenVerifier, TokenVerifier>();
        services.AddSingleton<RedirectVerifier>();
        services.AddSingleton<StaticHomePageNotice>();
        services.AddSingleton<LfsDownloadUrls>();
        services.AddSingleton<IGeneralRemoteDownloadUrls, GeneralRemoteDownloadUrls>();
        services.AddSingleton<ILocalTempFileLocks, LocalTempFileLocks>();
        services.AddSingleton<IRemoteResourceHashCalculator, RemoteResourceHashCalculator>();

        services.AddScoped<IPatreonAPI, PatreonAPI>();
        services.AddScoped<LfsRemoteStorage>();
        services.AddScoped<IGeneralRemoteStorage, GeneralRemoteStorage>();
        services.AddScoped<IDevForumAPI, DevForumAPI>();
        services.AddScoped<ICommunityForumAPI, CommunityForumAPI>();
        services.AddScoped<RemoteServerHandler>();
        services.AddScoped<IEC2Controller, EC2Controller>();
        services.AddScoped<IControlledServerSSHAccess, ControlledServerSSHAccess>();
        services.AddScoped<IExternalServerSSHAccess, ExternalServerSSHAccess>();
        services.AddScoped<IGithubCommitStatusReporter, GithubCommitStatusReporter>();
        services.AddScoped<DiscordNotifications>();
        services.AddScoped<IMailQueue, MailToQueueSender>();
        services.AddScoped<ICLASignatureStorage, CLASignatureStorage>();
        services.AddScoped<ICLAExemptions, CLAExemptions>();
        services.AddScoped<IStackwalk, Stackwalk>();
        services.AddScoped<IStackwalkSymbolPreparer, StackwalkSymbolPreparer>();
        services.AddScoped<BackupStorage>();
        services.AddScoped<BackupHandler>();
        services.AddScoped<IFileDownloader, FileDownloader>();

        // Prefer the queue sender to not make operations wait for emails to be sent
        services.AddScoped<IMailSender, MailSender>();
        services.AddScoped<EmailTokens>();

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

        // Always running discord bot in the background (only runs if configured in app settings)
        services.AddScoped<RevolutionaryDiscordBotService>();
        services.AddHostedService<BotServiceRunner>();
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
                    .UseSnakeCaseNamingConvention()
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
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        });

        app.Use(async (context, next) =>
        {
            context.Response.Headers.Add("X-Frame-Options", "DENY");
            await next();
        });

        if (!env.IsDevelopment())
        {
            // Response compression causes watch browser refresh middleware to fail to inject
            app.UseResponseCompression();
        }

        // Routing has to be initialized before the rate limiter is invoked
        app.UseRouting();

        // Authentication to specific routes setup
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

        // Auth for generated pages (non-API page gets)
        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments("/api") &&
                !context.Request.Path.StartsWithSegments("/hangfire"),
            appBuilder => { appBuilder.UseMiddleware<CookieOnlyBasicAuthenticationMiddleware>(); });

        // Enable our configured rate limiting options
        // This requires authentication to be already performed
        app.UseRateLimiter();

        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(60),
        });

        if (env.IsDevelopment())
        {
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

        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = new[]
            {
                new HangfireDashboardAuthorization(
                    app.ApplicationServices.GetRequiredService<ILogger<HangfireDashboardAuthorization>>()),
            },
        });

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

        SetupDefaultJobs(Configuration.GetSection("Tasks:CronJobs"));

        // Early load the registration status
        app.ApplicationServices.GetRequiredService<IRegistrationStatus>();

        // Early load the redirect verification to check that BaseUrl is set
        app.ApplicationServices.GetRequiredService<RedirectVerifier>();
    }

    private static void AddJobHelper<T>(string? schedule)
        where T : class, IJob
    {
        // SKip adding the job if the schedule is unset for it (note that this probably leaves the old schedule
        // hanging around if it was configured at least once)
        if (string.IsNullOrEmpty(schedule))
            return;

        // If the server restarts very fast this can get locked in a crashing spiral due to
        // Hangfire.PostgreSql.PostgreSqlDistributedLockException
        // TODO: perhaps we should add a try catch here and just log the errors if there are any?
        // See: https://github.com/frankhommers/Hangfire.PostgreSql/issues/119
        // The ID here needs to match what it was previously automatically to not create duplicate jobs when deploying
        // to an existing instance
        RecurringJob.AddOrUpdate<T>($"{typeof(T).Name}.Execute", x => x.Execute(CancellationToken.None), schedule);
    }

    private void SetupDefaultJobs(IConfigurationSection configurationSection)
    {
        AddJobHelper<SessionCleanupJob>(configurationSection["SessionCleanupJob"]);
        AddJobHelper<CheckAllSSOUsersJob>(configurationSection["CheckAllSSOUsers"]);
        AddJobHelper<RefreshPatronsJob>(configurationSection["RefreshPatrons"]);
        AddJobHelper<RefreshLFSProjectFileTreesJob>(configurationSection["RefreshLFSFileTrees"]);
        AddJobHelper<RefreshLFSObjectStatisticsJob>(configurationSection["RefreshLFSObjectStatistics"]);
        AddJobHelper<DetectStuckServersJob>(configurationSection["DetectStuckServers"]);
        AddJobHelper<DetectLeftOnServersJob>(configurationSection["DetectLeftOnServers"]);
        AddJobHelper<TerminateLongStoppedServersJob>(configurationSection["TerminateLongStoppedServers"]);
        AddJobHelper<ScheduleServerMaintenanceJob>(configurationSection["ScheduleServerMaintenance"]);
        AddJobHelper<TimeoutInProgressClAsJob>(configurationSection["TimeoutInProgressCLAs"]);
        AddJobHelper<CancelStuckMultipartUploadsJob>(configurationSection["CancelStuckMultipartUploads"]);
        AddJobHelper<RemoveOldCompletedMultipartUploadsJob>(
            configurationSection["RemoveOldCompletedMultipartUploads"]);
        AddJobHelper<DeleteAbandonedInProgressCLASignaturesJob>(
            configurationSection["DeleteAbandonedInProgressCLASignatures"]);
        AddJobHelper<DeleteStackwalkToolResultsJob>(configurationSection["DeleteStackwalkToolResults"]);
        AddJobHelper<DeleteOldDisabledSymbolsJob>(configurationSection["DeleteOldDisabledSymbols"]);
        AddJobHelper<CreateBackupJob>(configurationSection["CreateBackup"]);
        AddJobHelper<RunMarkedServerMaintenanceJob>(configurationSection["RunMarkedServerMaintenance"]);
        AddJobHelper<RefreshFeedsJob>(configurationSection["RefreshFeeds"]);
        AddJobHelper<CleanOldDevBuildsJob>(configurationSection["CleanOldDevBuilds"]);
        AddJobHelper<DeleteFailedItemVersionUploadsJob>(configurationSection["DeleteFailedItemVersionUploads"]);
        AddJobHelper<ItemMovedLocationClearJob>(configurationSection["ItemMovedLocationClear"]);
        AddJobHelper<CleanOldFileVersionsJob>(configurationSection["CleanOldFileVersions"]);
        AddJobHelper<PurgeOldDeletedFileVersionsJob>(configurationSection["PurgeOldDeletedFileVersions"]);
        AddJobHelper<PurgeOldDeletedFilesJob>(configurationSection["PurgeOldDeletedFiles"]);
        AddJobHelper<DeleteOldServerLogsJob>(configurationSection["DeleteOldServerLogs"]);
        AddJobHelper<DeleteOldActionLogsJob>(configurationSection["DeleteOldActionLogs"]);
        AddJobHelper<DeleteOldAdminActionLogsJob>(configurationSection["DeleteOldAdminActionLogs"]);
        AddJobHelper<DeleteOldCIJobOutputJob>(configurationSection["DeleteOldCIJobOutput"]);
        AddJobHelper<DeleteOldCIBuildsJob>(configurationSection["DeleteOldCIBuilds"]);

        BackgroundJob.Enqueue<CreateDefaultFoldersJob>(x => x.Execute(CancellationToken.None));

        // This is kept here if in the future more hashed fields are needed to be added so this might be needed
        // in the future as well to update info in the db
        // BackgroundJob.Enqueue<QueueRecomputeHashIfNeededJob>(x => x.Execute(CancellationToken.None));
    }
}
