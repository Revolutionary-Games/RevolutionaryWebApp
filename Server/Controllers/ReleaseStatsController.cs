namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared;
using Shared.Converters;
using Shared.Models;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class ReleaseStatsController : Controller
{
    private readonly ILogger<ReleaseStatsController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IHttpClientFactory httpClientFactory;

    public ReleaseStatsController(ILogger<ReleaseStatsController> logger, NotificationsEnabledDb database,
        IHttpClientFactory httpClientFactory)
    {
        this.logger = logger;
        this.database = database;
        this.httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    [ResponseCache(Duration = 900)]
    public async Task<ActionResult<List<RepoReleaseStats>>> Get()
    {
        var configs = await database.ReposForReleaseStats.AsNoTracking().OrderBy(r => r.QualifiedName)
            .Where(r => r.ShowInAll).ToListAsync();

        var result = new List<RepoReleaseStats>();

        foreach (var config in configs)
        {
            Regex? regex = null;

            if (config.IgnoreDownloads != null)
                regex = new Regex(config.IgnoreDownloads);

            result.Add(await FetchReleaseStats(config.QualifiedName, regex));
        }

        return result;
    }

    [HttpGet("{name}")]
    [ResponseCache(Duration = 900, VaryByQueryKeys = new[] { "name" })]
    public async Task<ActionResult<RepoReleaseStats>> GetSingle([Required] string name)
    {
        name = name.Replace(":", "/");

        var config = await database.ReposForReleaseStats.FindAsync(name);

        if (config == null)
            return NotFound();

        Regex? regex = null;

        if (config.IgnoreDownloads != null)
            regex = new Regex(config.IgnoreDownloads);

        return await FetchReleaseStats(config.QualifiedName, regex);
    }

    [HttpGet("config")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<PagedResult<RepoForReleaseStatsDTO>> GetConfiguration([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<RepoForReleaseStats> query;

        try
        {
            query = database.ReposForReleaseStats.AsNoTracking().OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpGet("config/{name}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<RepoForReleaseStatsDTO>> GetSingleConfig([Required] string name)
    {
        name = name.Replace(":", "/");

        var config = await database.ReposForReleaseStats.FindAsync(name);

        if (config == null)
            return NotFound();

        return config.GetDTO();
    }

    [HttpPost("config")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> Create([Required] [FromBody] RepoForReleaseStatsDTO request)
    {
        // Convert blank regex to null, this is due to how the input forms work on the client, so this makes that
        // not need a workaround
        if (string.IsNullOrEmpty(request.IgnoreDownloads))
            request.IgnoreDownloads = null;

        var config = await database.ReposForReleaseStats.FindAsync(request.QualifiedName);

        if (config != null)
            return BadRequest("Repository is already in use");

        var user = HttpContext.AuthenticatedUser()!;

        config = new RepoForReleaseStats(request.QualifiedName, request.ShownInAll)
        {
            IgnoreDownloads = request.IgnoreDownloads,
        };
        await database.ReposForReleaseStats.AddAsync(config);

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"New repo for release stats {config.QualifiedName} created",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Repo for release stats {QualifiedName} created by {Email}", config.QualifiedName,
            user.Email);

        return Ok();
    }

    [HttpDelete("config/{name}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> Delete([Required] string name)
    {
        name = name.Replace(":", "/");

        var config = await database.ReposForReleaseStats.FindAsync(name);

        if (config == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        database.ReposForReleaseStats.Remove(config);

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Repo for release stats ({config.QualifiedName}) deleted",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Repo for release stats {QualifiedName} deleted by {Email}", config.QualifiedName,
            user.Email);

        return Ok();
    }

    [NonAction]
    private async Task<RepoReleaseStats> FetchReleaseStats(string repo, Regex? ignoreDownloads)
    {
        var client = httpClientFactory.CreateClient("github");

        var releases = await client.GetFromJsonAsync<List<GithubRelease>>(QueryHelpers.AddQueryString(
            $"repos/{repo}/releases",
            new Dictionary<string, string?>
            {
                { "per_page", "100" },
            })) ?? throw new NullDecodedJsonException();

        // TODO: fetch more pages if there are more than a 100 releases
        if (releases.Count > 100)
        {
            logger.LogWarning("More than 100 Github releases, we need to implement paging");
        }

        var result = new RepoReleaseStats(repo);

        long totalDownloads = 0;
        long totalLinuxDownloads = 0;
        long totalWindowsDownloads = 0;
        long totalMacDownloads = 0;

        foreach (var release in releases)
        {
            CountReleaseDownloadStats(release, ignoreDownloads, ref totalDownloads, ref totalLinuxDownloads,
                ref totalWindowsDownloads,
                ref totalMacDownloads);
        }

        result.TotalDownloads = totalDownloads;
        result.TotalLinuxDownloads = totalLinuxDownloads;
        result.TotalWindowsDownloads = totalWindowsDownloads;
        result.TotalMacDownloads = totalMacDownloads;
        result.TotalReleases = releases.Count;

        var latestRelease = releases.MaxBy(r => r.PublishedAt);

        if (latestRelease != null)
        {
            result.LatestRelease = latestRelease.Name;
            result.LatestReleaseTime = latestRelease.PublishedAt;

            long total = 0;
            long linux = 0;
            long windows = 0;
            long mac = 0;
            CountReleaseDownloadStats(latestRelease, ignoreDownloads, ref total, ref linux, ref windows, ref mac);

            result.LatestDownloads = total;
            result.LatestLinuxDownloads = linux;
            result.LatestWindowsDownloads = windows;
            result.LatestMacDownloads = mac;

            var elapsedDays = (int)Math.Floor((DateTime.UtcNow - result.LatestReleaseTime.Value).TotalDays);

            if (elapsedDays < 1)
                elapsedDays = 1;

            result.LatestDownloadsPerDay = total / elapsedDays;
        }

        return result;
    }

    private void CountReleaseDownloadStats(GithubRelease release, Regex? ignoreDownloads, ref long total,
        ref long linux, ref long windows, ref long mac)
    {
        foreach (var asset in release.Assets)
        {
            var assetName = asset.Name.ToLowerInvariant();
            if (ignoreDownloads != null && ignoreDownloads.IsMatch(assetName))
                continue;

            total += asset.DownloadCount;

            if (assetName.Contains("mac") || assetName.Contains(".dmg"))
            {
                mac += asset.DownloadCount;
            }
            else if (assetName.Contains("windows") || assetName.Contains(".exe") ||
                     (assetName.Contains("win") && !assetName.Contains("linux")))
            {
                windows += asset.DownloadCount;
            }
            else
            {
                linux += asset.DownloadCount;
            }
        }
    }
}
