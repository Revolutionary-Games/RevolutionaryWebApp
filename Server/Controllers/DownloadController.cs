namespace ThriveDevCenter.Server.Controllers;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using DevCenterCommunication.Models;
using Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/download")]
public class DownloadController : Controller
{
    private readonly ILogger<DownloadController> logger;
    private readonly ApplicationDbContext database;
    private readonly IGeneralRemoteDownloadUrls remoteDownload;

    public DownloadController(ILogger<DownloadController> logger, ApplicationDbContext database,
        IGeneralRemoteDownloadUrls remoteDownload)
    {
        this.logger = logger;
        this.database = database;
        this.remoteDownload = remoteDownload;
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult> DownloadStorageItem([Required] long id, int? version)
    {
        if (!remoteDownload.Configured)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status500InternalServerError,
                Value = "Remote storage on the server is not configured",
            };
        }

        var (user, restriction) = HttpContext.AuthenticatedUserWithRestriction();

        if (user != null && restriction != AuthenticationScopeRestriction.None)
            return this.WorkingForbid("Your login authentication method restricts access to this endpoint");

        var item = await database.StorageItems.FindAsync(id);

        if (item == null || !item.IsReadableBy(user))
            return NotFound("File not found or you don't have access to it. Logging in may help.");

        var latestUploaded = await item.GetHighestUploadedVersion(database);

        StorageItemVersion? toDownload;

        if (version != null)
        {
            // Access to specific version
            var wantedVersion = await database.StorageItemVersions.Include(v => v.StorageFile)
                .Where(v => v.StorageItem == item && v.Version == version).FirstOrDefaultAsync();

            if (wantedVersion == null || latestUploaded == null || wantedVersion.Id != latestUploaded.Id)
            {
                // Non-latest uploaded file, need access
                if (user == null || !user.HasAccessLevel(UserAccessLevel.RestrictedUser))
                    return this.WorkingForbid("You need to login to access non-latest versions of files.");
            }

            toDownload = wantedVersion;
        }
        else
        {
            toDownload = latestUploaded;
        }

        if (toDownload == null)
            return NotFound("File version not found");

        if (toDownload.StorageFile == null)
        {
            logger.LogWarning("StorageItem {Id} has a version {Version} that has no associated storage file",
                item.Id, toDownload.Version);
            return NotFound("File version has no associated storage item");
        }

        return Redirect(remoteDownload.CreateDownloadFor(toDownload.StorageFile,
            AppInfo.RemoteStorageDownloadExpireTime));
    }

    [HttpGet("patreonCredits")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<PatreonCredits>> DownloadPatronCredits()
    {
        var patrons = await database.Patrons.Where(p => p.Suspended != true).ToListAsync();

        var patreonSettings = await database.PatreonSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();

        if (patreonSettings == null)
            return Problem("Patreon settings not found");

        logger.LogInformation("Patron list for credits has been accessed by {Email}",
            HttpContext.AuthenticatedUser()!.Email);

        var groups = patrons.GroupBy(p => p.RewardId).ToList();

        var vips = groups.FirstOrDefault(g => g.Key == patreonSettings.VipRewardId);
        var devbuilds = groups.FirstOrDefault(g => g.Key == patreonSettings.DevbuildsRewardId);
        var other = groups.FirstOrDefault(g =>
            g.Key != patreonSettings.VipRewardId && g.Key != patreonSettings.DevbuildsRewardId);

        var result = new PatreonCredits
        {
            VIPPatrons = PreparePatronGroup(vips),
            DevBuildPatrons = PreparePatronGroup(devbuilds),
            SupporterPatrons = PreparePatronGroup(other),
        };

        Response.Headers.Add("Content-Disposition", "attachment; filename=\"patrons.json\"");
        return result;
    }

    [NonAction]
    private List<string> PreparePatronGroup(IGrouping<string, Patron>? group)
    {
        if (group == null)
            return new List<string>();

        return group.OrderBy(p => p.Username).Select(p => p.Username).ToList();
    }
}
