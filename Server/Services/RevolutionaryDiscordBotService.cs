namespace ThriveDevCenter.Server.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Html.Parser;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Converters;
using Shared.Utilities;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Svg.Skia;
using Utilities;
using Color = Discord.Color;
using Image = SixLabors.ImageSharp.Image;

/// <summary>
///   Handles running the Revolutionary Bot for Discord. Used on the Thrive community discord
/// </summary>
public class RevolutionaryDiscordBotService
{
    private const int SecondsBetweenSameCommand = 10;

    private static readonly TimeSpan
        CommandIntervalBeforeRunningAgain = TimeSpan.FromSeconds(SecondsBetweenSameCommand);

    private readonly ILogger<RevolutionaryDiscordBotService> logger;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ApplicationDbContext database;

    /// <summary>
    ///   Used to limit how many tasks are at once running expensive operations
    /// </summary>
    private readonly SemaphoreSlim expensiveOperationLimiter = new(1);

    private readonly Dictionary<string, DateTime> lastRanCommands = new();

    private readonly string botToken;
    private readonly ulong? primaryGuild;
    private readonly Uri wikiUrlBase;
    private readonly Uri overallTranslationStatusUrl;
    private readonly Uri translationProgressUrl;
    private readonly Uri wikiDefaultPreviewImage;
    private readonly Uri progressFontUrl;
    private readonly Uri progressImageUrl;

    private DiscordSocketClient? client;

    private TimedResourceCache<List<GithubMilestone>>? githubMilestones;
    private Lazy<Task<FontCollection>>? fonts;
    private DisposableTimedResourceCache<Image>? progressBackgroundImage;

    private DisposableTimedResourceCache<Image>? overallTranslationStatus;
    private DisposableTimedResourceCache<Image>? translationProgress;

    public RevolutionaryDiscordBotService(ILogger<RevolutionaryDiscordBotService> logger, IConfiguration configuration,
        IHttpClientFactory httpClientFactory, ApplicationDbContext database)
    {
        this.logger = logger;
        this.httpClientFactory = httpClientFactory;
        this.database = database;

        botToken = configuration["Discord:RevolutionaryBot:Token"];
        var guild = configuration["Discord:RevolutionaryBot:PrimaryGuild"];

        wikiUrlBase = new Uri(configuration["Discord:RevolutionaryBot:WikiBaseUrl"]);
        translationProgressUrl = new Uri(configuration["Discord:RevolutionaryBot:TranslationProgressUrl"]);
        overallTranslationStatusUrl = new Uri(configuration["Discord:RevolutionaryBot:OverallTranslationStatusUrl"]);
        wikiDefaultPreviewImage =
            new Uri(configuration["Discord:RevolutionaryBot:WikiDefaultPreviewImage"]);
        progressFontUrl = configuration.BaseUrlRelative("Discord:RevolutionaryBot:ProgressFont");
        progressImageUrl = configuration.BaseUrlRelative("Discord:RevolutionaryBot:ProgressBackgroundImage");

        if (string.IsNullOrEmpty(botToken))
            return;

        if (guild != null)
            primaryGuild = ulong.Parse(guild);

        Configured = true;
    }

    public bool Configured { get; }

    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Configured)
        {
            logger.LogInformation("Revolutionary Bot is not configured for use");
            return;
        }

        SetupCachedResourceFetching();

        // Then start the Discord bot part to make sure our resources are setup before the first callbacks from it
        logger.LogInformation("Revolutionary Bot for Discord is starting");
        client = new DiscordSocketClient();
        client.Log += Log;
        client.Ready += ClientReady;
        client.SlashCommandExecuted += SlashCommandHandler;

        await client.LoginAsync(TokenType.Bot, botToken);
        await client.StartAsync();

        // Wait until we are stopped
        await Task.Delay(-1, stoppingToken);

        await client.StopAsync();
    }

    private void SetupCachedResourceFetching()
    {
        // Load fonts once
        fonts = new Lazy<Task<FontCollection>>(() => DownloadFont(progressFontUrl));

        // Setup other cached resource loading
        progressBackgroundImage =
            new DisposableTimedResourceCache<Image>(() => DownloadImage(progressImageUrl), TimeSpan.FromMinutes(15));

        overallTranslationStatus =
            new DisposableTimedResourceCache<Image>(() => DownloadImage(overallTranslationStatusUrl),
                TimeSpan.FromMinutes(5));
        translationProgress = new DisposableTimedResourceCache<Image>(
            () => DownloadSvg(translationProgressUrl), TimeSpan.FromMinutes(5));

        githubMilestones = new TimedResourceCache<List<GithubMilestone>>(async () =>
        {
            // TODO: if we have more than 100 milestones only the latest 100 can be used like this
            var httpClient = httpClientFactory.CreateClient("github");

            return await httpClient.GetFromJsonAsync<List<GithubMilestone>>(QueryHelpers.AddQueryString(
                "repos/Revolutionary-Games/Thrive/milestones", new Dictionary<string, string?>
                {
                    { "state", "all" },
                    { "per_page", "100" },
                    { "direction", "desc" },
                })) ?? throw new NullDecodedJsonException();
        }, TimeSpan.FromMinutes(1));
    }

    private async Task ClientReady()
    {
        if (primaryGuild != null)
        {
            try
            {
                var guild = client!.GetGuild(primaryGuild.Value);

                await guild.CreateApplicationCommandAsync(BuildProgressCommand().Build());
                await guild.CreateApplicationCommandAsync(BuildLanguageCommand().Build());
                await guild.CreateApplicationCommandAsync(BuildWikiCommand().Build());
            }
            catch (HttpException exception)
            {
                logger.LogError(exception, "Failed to register one or more guild commands");
            }
        }

        // TODO: uncomment
        // try
        // {
        //     // TODO: only call the command building once for each command as recommended
        //     await client.CreateGlobalApplicationCommandAsync(BuildProgressCommand().Build());
        // }
        // catch (HttpException exception)
        // {
        //     logger.LogError(exception, "Failed to register progress command");
        // }

        /*commandBuilder = new SlashCommandBuilder()
            .WithName("help")
            .WithDescription("Displays help for this bot");

        try
        {
            await client!.CreateGlobalApplicationCommandAsync(commandBuilder.Build());
        }
        catch (HttpException exception)
        {
            logger.LogError(exception, "Failed to register help command");
        }*/
    }

    private async Task SlashCommandHandler(SocketSlashCommand command)
    {
        switch (command.Data.Name)
        {
            case "progress":
                await HandleProgressCommand(command);
                break;
            case "language":
                await HandleLanguageCommand(command);
                break;
            case "wiki":
                await HandleWikiCommand(command);
                break;
            default:
                await command.RespondAsync("Unknown command! Type `/` to see available commands");
                break;
        }
    }

    private async Task HandleProgressCommand(SocketSlashCommand command)
    {
        var versionOption = command.Data.Options.FirstOrDefault();

        string? version = null;

        if (versionOption is { Type: ApplicationCommandOptionType.String })
        {
            version = (string)versionOption.Value;

            if (string.IsNullOrWhiteSpace(version))
            {
                version = null;
            }
        }

        if (!await CheckCanRunAgain(command, $"progress-{version}"))
            return;

        await command.DeferAsync();

#pragma warning disable CS4014 // we don't want to hold up the command processing
        PerformSlowProgressPart(command, version);
#pragma warning restore CS4014
    }

    private async Task PerformSlowProgressPart(SocketSlashCommand command, string? version)
    {
        List<GithubMilestone> milestones;
        try
        {
            milestones = await githubMilestones!.GetData();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Github connection error");
            await command.ModifyOriginalResponseAsync(properties => properties.Content = "Query to Github failed");
            return;
        }

        GithubMilestone? milestone;

        if (version == null)
        {
            milestone = milestones.MaxBy(m => m.DueOn);
        }
        else
        {
            milestone = milestones.OrderByDescending(m => m.DueOn).FirstOrDefault(m => m.Title.Contains(version));
        }

        if (milestone == null)
        {
            await command.ModifyOriginalResponseAsync(properties => properties.Content = "No matching milestone found");
            return;
        }

        var totalIssues = milestone.OpenIssues + milestone.ClosedIssues;

        var completionFraction = totalIssues > 0 ? (float)milestone.ClosedIssues / totalIssues : 0.0f;
        var percentage = (float)Math.Round(completionFraction * 100);

        var openText = "open issue".PrintCount(milestone.OpenIssues);
        var closedText = "closed issue".PrintCount(milestone.ClosedIssues);

        var embedBuilder = new EmbedBuilder().WithTitle(milestone.Title)
            .WithDescription($"{percentage}% done with {openText} {closedText}")
            .WithTimestamp(milestone.UpdatedAt).WithUrl(milestone.HtmlUrl);

        if (milestone.DueOn != null)
            embedBuilder.WithFooter($"Due by {milestone.DueOn.Value:yyyy-MM-dd}");

        Image backgroundImage;
        FontCollection fontCollection;

        try
        {
            var fontWaitTask = fonts!.Value;
            backgroundImage = await progressBackgroundImage!.GetData();
            fontCollection = await fontWaitTask;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error getting progress image resources");
            await command.ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = "Failed to get progress image drawing resources";
                properties.Embeds = new[] { embedBuilder.Build() };
            });
            return;
        }

        using var tempFileData = new MemoryStream();

        await expensiveOperationLimiter.WaitAsync();
        try
        {
            // We assume the size of the background image here
            int width = 700;
            int height = 300;

            var titleFont = fontCollection.Families.First().CreateFont(56, FontStyle.Bold);
            var percentageFont = fontCollection.Families.First().CreateFont(36, FontStyle.Bold);
            var issueCountFont = fontCollection.Families.First().CreateFont(36, FontStyle.Bold);

            using var progressImage = new Image<Rgb24>(width, height);

            // ReSharper disable AccessToDisposedClosure
            // Draw the background image
            progressImage.Mutate(x => { x.DrawImage(backgroundImage, 1); });

            // ReSharper restore AccessToDisposedClosure

            progressImage.Mutate(x =>
            {
                var textBrush = Brushes.Solid(SixLabors.ImageSharp.Color.White);
                var titlePen = Pens.Solid(SixLabors.ImageSharp.Color.Black, 3.0f);
                var textPen = Pens.Solid(SixLabors.ImageSharp.Color.Black, 2.0f);

                x.DrawText(milestone.Title, titleFont, textBrush, titlePen, new PointF(15, 5));

                // Issues
                var issuesY = 90;
                x.DrawText(openText, issueCountFont, textBrush, textPen, new PointF(30, issuesY));
                x.DrawText(closedText, issueCountFont, textBrush, textPen, new PointF(350, issuesY));

                // Progress bar
                // TODO: make rounded corners for the bar
                var barPen = Pens.Solid(SixLabors.ImageSharp.Color.Black, 7.0f);
                var barBrush = Brushes.Solid(new SixLabors.ImageSharp.Color(new Rgb24(63, 169, 82)));

                // TODO: gradient to:
                // new SixLabors.ImageSharp.Color(new Rgb24(134, 207, 147)));

                x.Fill(barBrush, new RectangularPolygon(30, 150, (width - 60) * completionFraction, 50));
                x.Draw(barPen, new RectangularPolygon(30, 150, width - 60, 50));

                // Percentage
                x.DrawText($"{percentage}%", percentageFont, textBrush, textPen, new PointF(50, 210));
            });

            await progressImage.SaveAsync(tempFileData, PngFormat.Instance);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Progress image drawing problem");
            await command.ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = "Failed to draw progress image";
                properties.Embeds = new[] { embedBuilder.Build() };
            });
            return;
        }
        finally
        {
            expensiveOperationLimiter.Release();
        }

        tempFileData.Position = 0;
        await command.FollowupWithFileAsync(tempFileData, "progress.png", string.Empty, new[] { embedBuilder.Build() });
    }

    private SlashCommandBuilder BuildProgressCommand()
    {
        return new SlashCommandBuilder()
            .WithName("progress")
            .WithDescription(
                "Displays the progress to the next release or, if a version number is specified, for that version.")
            .AddOption("version", ApplicationCommandOptionType.String, "The version to display progress for", false)
            .WithDMPermission(false);
    }

    private async Task HandleLanguageCommand(SocketSlashCommand command)
    {
        if (!await CheckCanRunAgain(command))
            return;

        await command.DeferAsync();

#pragma warning disable CS4014 // we don't want to hold up the command processing
        PerformSlowLanguagePart(command);
#pragma warning restore CS4014
    }

    private async Task PerformSlowLanguagePart(SocketSlashCommand command)
    {
        Image overallStatusImage;
        Image progressImage;

        try
        {
            var overallWaitTask = overallTranslationStatus!.GetData();
            progressImage = await translationProgress!.GetData();
            overallStatusImage = await overallWaitTask;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Weblate connection error");
            await command.ModifyOriginalResponseAsync(properties =>
                properties.Content = "Failed to get data from Weblate");
            return;
        }

        using var tempFileData = new MemoryStream();

        await expensiveOperationLimiter.WaitAsync();
        try
        {
            // We assume the overall status image is narrower
            using var languagesImage = new Image<Rgb24>(progressImage.Width + 10,
                overallStatusImage.Height + progressImage.Height + 15);

            // Clear everything to white first before drawing
            languagesImage.ProcessPixelRows(accessor =>
            {
                var white = new Rgb24(255, 255, 255);

                for (int y = 0; y < accessor.Height; y++)
                {
                    foreach (ref Rgb24 pixel in accessor.GetRowSpan(y))
                    {
                        pixel = white;
                    }
                }
            });

            // ReSharper disable AccessToDisposedClosure
            languagesImage.Mutate(x =>
            {
                x.DrawImage(progressImage, new Point(5, overallStatusImage.Height + 10), 1);
            });

            languagesImage.Mutate(x =>
                x.DrawImage(overallStatusImage, new Point(languagesImage.Width / 2 - overallStatusImage.Width / 2, 5),
                    1));

            // ReSharper restore AccessToDisposedClosure
            await languagesImage.SaveAsync(tempFileData, PngFormat.Instance);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Language image drawing problem");
            await command.ModifyOriginalResponseAsync(
                properties => properties.Content = "Failed to draw language progress image");
            return;
        }
        finally
        {
            expensiveOperationLimiter.Release();
        }

        tempFileData.Position = 0;
        await command.FollowupWithFileAsync(tempFileData, "language.png");
    }

    private SlashCommandBuilder BuildLanguageCommand()
    {
        return new SlashCommandBuilder()
            .WithName("language")
            .WithDescription(
                "Displays current language translation statistics for Thrive.");
    }

    private async Task HandleWikiCommand(SocketSlashCommand command)
    {
        var pageOption = command.Data.Options.FirstOrDefault();

        string? page = null;

        if (pageOption is { Type: ApplicationCommandOptionType.String })
        {
            page = (string)pageOption.Value;

            // Convert spaces to underscores to make the links work
            // TODO: somehow also fix capitalization
            page = page.Replace(' ', '_');
        }

        if (string.IsNullOrWhiteSpace(page))
        {
            await command.RespondAsync("Please provide a wiki page title");
            return;
        }

        if (page.Contains("../"))
        {
            await command.RespondAsync("Invalid page title");
            return;
        }

        if (!await CheckCanRunAgain(command, page))
            return;

        await command.DeferAsync();

        var httpClient = httpClientFactory.CreateClient();

        var finalUrl = new Uri(wikiUrlBase, page);

        HttpResponseMessage response;

        try
        {
            response = await httpClient.GetAsync(finalUrl);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to connect to the wiki");
            await command.ModifyOriginalResponseAsync(properties =>
                properties.Content = "Failed to connect to the wiki");
            return;
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            await command.ModifyOriginalResponseAsync(properties =>
                properties.Content =
                    "The requested wiki page does not exist. Note that multi word titles are case sensitive");
            return;
        }

        DateTime? updatedTime = null;

        if (response.Content.Headers.TryGetValues("Last-Modified", out var lastModifiedHeader))
        {
            var rawDate = lastModifiedHeader.FirstOrDefault();

            if (!string.IsNullOrEmpty(rawDate))
            {
                if (!DateTime.TryParse(rawDate, out var parsed))
                {
                    logger.LogError("Failed to parse wiki modified date from: {RawDate}", rawDate);
                }
                else
                {
                    updatedTime = parsed;
                }
            }
        }

        var parser = new HtmlParser();
        var document = parser.ParseDocument(await response.Content.ReadAsStringAsync());

        var title = document.Title ?? "Unknown title";

        var description = "Page description not found";

        var descriptionElement = document.QuerySelectorAll("meta[name=\"description\"]")
            .FirstOrDefault();

        if (descriptionElement != null)
            description = descriptionElement.GetAttribute("content") ?? "Content attribute not found";

        // Primary content could be parsed inside div id="mw-content-text"

        var embedBuilder = new EmbedBuilder().WithTitle(title).WithDescription(description)
            .WithImageUrl(wikiDefaultPreviewImage.ToString()).WithUrl(finalUrl.ToString())
            .WithColor(new Color(182, 9, 9));

        if (updatedTime != null)
            embedBuilder.WithTimestamp(updatedTime.Value);

        await command.ModifyOriginalResponseAsync(properties =>
        {
            properties.Embeds = new[] { embedBuilder.Build() };
        });
    }

    private SlashCommandBuilder BuildWikiCommand()
    {
        return new SlashCommandBuilder()
            .WithName("wiki")
            .WithDescription(
                "Get a summary of a Thrive wiki page.")
            .AddOption("page-title", ApplicationCommandOptionType.String, "The version to display progress for", true);
    }

    private Task Log(LogMessage message)
    {
        var translatedLevel = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Debug => LogLevel.Debug,
            LogSeverity.Verbose => LogLevel.Trace,
            _ => throw new ArgumentOutOfRangeException(),
        };

        logger.Log(translatedLevel, message.Exception, "{Source}: {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }

    private async Task<bool> CheckCanRunAgain(SocketSlashCommand command, string? overrideKey = null)
    {
        var key = overrideKey ?? command.CommandName;
        var now = DateTime.Now;

        if (lastRanCommands.TryGetValue(key, out var lastRan) &&
            now - lastRan < CommandIntervalBeforeRunningAgain)
        {
            await command.RespondAsync(
                $"Patience please, that command was used in the last {SecondsBetweenSameCommand} seconds");
            return false;
        }

        lastRanCommands[key] = now;
        return true;
    }

    private async Task<FontCollection> DownloadFont(Uri url)
    {
        var httpClient = httpClientFactory.CreateClient();

        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStreamAsync();

        var collection = new FontCollection();
        collection.Add(content);

        return collection;
    }

    private async Task<Image> DownloadImage(Uri url)
    {
        var httpClient = httpClientFactory.CreateClient();

        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStreamAsync();

        return await Image.LoadAsync(content);
    }

    private async Task<Image> DownloadSvg(Uri url)
    {
        var httpClient = httpClientFactory.CreateClient();

        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStreamAsync();

        using var svg = new SKSvg();
        using var svgAsPngData = new MemoryStream();

        if (!(svg.Load(content) is { }))
            throw new Exception("Failed to load the svg data");

        svg.Picture!.ToImage(svgAsPngData, SKColor.Empty, SKEncodedImageFormat.Png, 100, 1.0f, 1.0f,
            SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());

        // Reset the stream position after writing, this is very important to make sure this works
        svgAsPngData.Position = 0;
        return await Image.LoadAsync(svgAsPngData, new PngDecoder());
    }
}
