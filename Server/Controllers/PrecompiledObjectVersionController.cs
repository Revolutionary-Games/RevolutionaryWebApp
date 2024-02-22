namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using DevCenterCommunication.Models;
using DevCenterCommunication.Models.Enums;
using Filters;
using Hangfire;
using Jobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using RecursiveDataAnnotationsValidation;
using Services;
using Shared;
using Shared.Forms;
using Shared.Models;
using Shared.Models.Enums;
using SharedBase.Models;
using SharedBase.Utilities;
using Utilities;

[ApiController]
[Route("api/v1/PrecompiledObject/{objectId:long}/versions")]
public class PrecompiledObjectVersionController : Controller
{
    private const string SymbolUploadProtectionPurposeString = "PrecompiledObject.Upload.v1";

    private readonly ILogger<PrecompiledObjectVersionController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IGeneralRemoteStorage remoteStorage;
    private readonly IGeneralRemoteDownloadUrls downloadUrls;
    private readonly IBackgroundJobClient jobClient;
    private readonly IDataProtector dataProtector;

    public PrecompiledObjectVersionController(ILogger<PrecompiledObjectVersionController> logger,
        NotificationsEnabledDb database, IGeneralRemoteStorage remoteStorage, IGeneralRemoteDownloadUrls downloadUrls,
        IBackgroundJobClient jobClient, IDataProtectionProvider dataProtectionProvider)
    {
        this.logger = logger;
        this.database = database;
        this.remoteStorage = remoteStorage;
        this.downloadUrls = downloadUrls;
        this.jobClient = jobClient;
        dataProtector = dataProtectionProvider.CreateProtector(SymbolUploadProtectionPurposeString);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<PrecompiledObjectVersionDTO>>> Get([Required] long objectId,
        [Required] string sortColumn, [Required] SortDirection sortDirection,
        [Required] [Range(1, int.MaxValue)] int page, [Required] [Range(1, 100)] int pageSize)
    {
        var precompiled = await TryGetVisiblePrecompiled(objectId);

        if (precompiled == null)
            return NotFound();

        IQueryable<PrecompiledObjectVersion> query;

        try
        {
            query = database.PrecompiledObjectVersions.Where(v => v.OwnedById == precompiled.Id)
                .OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i =>
            i.GetDTO(HttpContext.HasAuthenticatedUserWithAccessLevelExtended(GroupType.RestrictedUser, null) ==
                HttpContextAuthorizationExtensions.AuthenticationResult.Success));
    }

    [HttpPost("offerVersion")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    public async Task<ActionResult> OfferVersion([Required] long objectId,
        [Required] [FromBody] PrecompiledObjectVersionDTO request)
    {
        var validator = new RecursiveDataAnnotationValidator();
        var validations = new List<ValidationResult>();

        if (!validator.TryValidateObjectRecursive(request, validations))
        {
            return BadRequest("Bad offered version data");
        }

        var precompiled = await TryGetVisiblePrecompiled(objectId);

        if (precompiled == null)
            return NotFound();

        if (await CheckIfVersionAlreadyExists(precompiled, request))
        {
            return NoContent();
        }

        return Ok("Proceed to upload");
    }

    [HttpPost("startUpload")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    public async Task<ActionResult<UploadRequestResponse>> StartUpload([Required] long objectId,
        [Required] [FromBody] PrecompiledObjectVersionDTO request)
    {
        var validator = new RecursiveDataAnnotationValidator();
        var validations = new List<ValidationResult>();

        if (!validator.TryValidateObjectRecursive(request, validations))
        {
            return BadRequest("Bad offered version data");
        }

        if (request.Size < 10)
            return BadRequest("Size must be specified before upload");

        if (request.Size > GlobalConstants.MEBIBYTE * 1000)
            return BadRequest("Precompiled size is too large. Max is 1000 Mebibytes");

        var precompiled = await TryGetVisiblePrecompiled(objectId);

        if (precompiled == null)
            return NotFound();

        if (await CheckIfVersionAlreadyExists(precompiled, request))
        {
            return Conflict("Precompiled object already created / uploading currently");
        }

        if (!remoteStorage.Configured)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status500InternalServerError,
                Value = "Remote storage is not configured",
            };
        }

        var folder = await StorageItem.GetPrecompiledFolder(database);

        if (folder == null)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status500InternalServerError,
                Value = "Storage folder is missing",
            };
        }

        var user = HttpContext.AuthenticatedUserOrThrow();

        var precompiledVersion = new PrecompiledObjectVersion(precompiled.Id, request.Version)
        {
            Platform = request.Platform,
            Tags = request.Tags,
            Size = request.Size,
            Uploaded = false,
            CreatedById = user.Id,
        };

        var storageItem = new StorageItem
        {
            Name = precompiledVersion.StorageFileName,
            Parent = folder,
            Ftype = FileType.File,
            Special = true,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.Nobody,
        };

        precompiledVersion.StoredInItem = storageItem;

        await database.StorageItems.AddAsync(storageItem);
        await database.PrecompiledObjectVersions.AddAsync(precompiledVersion);

        // This save will fail if duplicate version was attempted
        await database.SaveChangesAsync();

        jobClient.Enqueue<CountFolderItemsJob>(x => x.Execute(folder.Id, CancellationToken.None));

        logger.LogInformation(
            "New PrecompiledObject version ({Id}) {Version} for {Platform} (tags: {Tags}) created by {Email}",
            precompiledVersion.OwnedById, precompiledVersion.Version, precompiledVersion.Platform,
            precompiledVersion.Tags, user.Email);

        // Create a version to upload to
        var version = await precompiledVersion.StoredInItem.CreateNextVersion(database, user);

        var file = await version.CreateStorageFile(database, DateTime.UtcNow + AppInfo.RemoteStorageUploadExpireTime,
            request.Size);

        if (request.Size != file.Size)
            throw new Exception("Logic error in StorageFile size setting");

        await database.SaveChangesAsync();

        logger.LogInformation("Upload of PrecompiledObject {StorageName} starting from {RemoteIpAddress}",
            precompiledVersion.StorageFileName, HttpContext.Connection.RemoteIpAddress);

        jobClient.Schedule<DeletePrecompiledObjectVersionIfUploadFailed>(
            x => x.Execute(precompiledVersion.OwnedById, precompiledVersion.Version, precompiledVersion.Platform,
                precompiledVersion.Tags, CancellationToken.None),
            AppInfo.RemoteStorageUploadExpireTime * 2);

        return new UploadRequestResponse
        {
            UploadUrl = remoteStorage.CreatePresignedUploadURL(file.UploadPath,
                AppInfo.RemoteStorageUploadExpireTime),
            VerifyToken = new StorageUploadVerifyToken(dataProtector, file.UploadPath, file.StoragePath,
                file.Size.Value, file.Id, null, null, null)
            {
                // Version is last here to make sure version having ':' can't break the parsing
                ExtraDataStore =
                    $"{precompiledVersion.OwnedById}:{(int)precompiledVersion.Platform}:" +
                    $"{(int)precompiledVersion.Tags}:{precompiledVersion.Version}",
            }.ToString(),
        };
    }

    [HttpPost("finishUpload")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    public async Task<ActionResult> FinishUpload([Required] [FromBody] TokenForm request)
    {
        if (!remoteStorage.Configured)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status500InternalServerError,
                Value = "Remote storage is not configured",
            };
        }

        var decodedToken = StorageUploadVerifyToken.TryToLoadFromString(dataProtector, request.Token);

        if (decodedToken?.ExtraDataStore == null || decodedToken.ExtraDataStore.Count(c => c == ':') < 3)
            return BadRequest("Invalid finished upload token");

        StorageFile file;
        try
        {
            file = await remoteStorage.HandleFinishedUploadToken(database, decodedToken);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to check upload token / resulting file");
            return BadRequest("Failed to verify that uploaded file is valid");
        }

        // This is used in the token here to smuggle all the data needed to find the precompiled object
        var parts = decodedToken.ExtraDataStore.Split(':', 4);

        var parentId = long.Parse(parts[0]);
        var platform = (PackagePlatform)int.Parse(parts[1]);
        var tags = (PrecompiledTag)int.Parse(parts[2]);
        var versionStr = parts[3];

        var version = await database.PrecompiledObjectVersions.FindAsync(parentId, versionStr, platform, tags);

        if (version == null)
            return BadRequest("No precompiled object version found with the identifier in the token");

        version.Uploaded = true;

        await remoteStorage.PerformFileUploadSuccessActions(file, database);
        await database.SaveChangesAsync();

        logger.LogInformation("PrecompiledObjectVersion {StoragePath} is now uploaded", file.StoragePath);

        jobClient.Enqueue<CountPrecompiledObjectSizeJob>(x => x.Execute(parentId, CancellationToken.None));

        return Ok();
    }

    [HttpGet("{version}/{platform:int}/{tags:int}")]
    public async Task<IActionResult> DownloadVersion([Required] long objectId,
        [Required] string version, [Required] int platform, [Required] int tags)
    {
        if (version.Length > 200)
            return BadRequest("Too long version provided");

        var objectVersion = await GetVersionForDownload(objectId, version, platform, tags);

        if (objectVersion == null)
            return NotFound();

        return Redirect(await GetDownloadLink(objectVersion));
    }

    [HttpGet("{version}/{platform:int}/{tags:int}/link")]
    public async Task<IActionResult> GetDownloadUrl([Required] long objectId,
        [Required] string version, [Required] int platform, [Required] int tags)
    {
        if (version.Length > 200)
            return BadRequest("Too long version provided");

        var objectVersion = await GetVersionForDownload(objectId, version, platform, tags);

        if (objectVersion == null)
            return NotFound();

        return Ok(await GetDownloadLink(objectVersion));
    }

    [HttpGet("{version}/{platform:int}/{tags:int}/info")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<ActionResult<PrecompiledObjectVersionDTO>> GetSingle([Required] long objectId,
        [Required] string version, [Required] int platform, [Required] int tags)
    {
        if (version.Length > 200)
            return BadRequest("Too long version provided");

        var precompiled = await TryGetVisiblePrecompiled(objectId);

        if (precompiled == null)
            return NotFound();

        var objectVersion = await database.PrecompiledObjectVersions.FindAsync(
            precompiled.Id, version, (PackagePlatform)platform, (PrecompiledTag)tags);

        if (objectVersion == null)
            return NotFound("No such version");

        // This endpoint requires authenticated user
        return objectVersion.GetDTO(true);
    }

    [HttpDelete("{version}/{platform:int}/{tags:int}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    public async Task<IActionResult> DeleteVersion([Required] long objectId,
        [Required] string version, [Required] int platform, [Required] int tags)
    {
        if (version.Length > 200)
            return BadRequest("Too long version provided");

        var parsedPlatform = (PackagePlatform)platform;
        var parsedTags = (PrecompiledTag)tags;

        var precompiled = await TryGetVisiblePrecompiled(objectId);

        if (precompiled == null)
            return NotFound();

        var objectVersion = await database.PrecompiledObjectVersions.Include(v => v.StoredInItem)
            .ThenInclude(i => i.StorageItemVersions).FirstOrDefaultAsync(
                v => v.OwnedById == precompiled.Id && v.Version == version && v.Platform == parsedPlatform &&
                    v.Tags == parsedTags);

        if (objectVersion == null)
            return NotFound("No such version");

        var user = HttpContext.AuthenticatedUserOrThrow();

        // TODO: is this restriction sensible?
        if (objectVersion.CreatedById != user.Id && !user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
        {
            return this.WorkingForbid("Only uploader or admin can delete an uploaded version of an object");
        }

        string extra = string.Empty;

        if (!objectVersion.Uploaded)
        {
            extra = " (was deleted while still uploading)";
        }

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = $"PrecompiledObjectVersion {objectVersion.StorageFileName} deleted{extra}",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        DeletePrecompiledObjectVersionIfUploadFailed.DeletePrecompiledObjectVersion(objectVersion, jobClient);

        logger.LogInformation("PrecompiledObjectVersion {Identifier} deleted by {Email}", objectVersion.StorageFileName,
            user.Email);

        return Ok("Object will be deleted in a few minutes");
    }

    [NonAction]
    private async Task<PrecompiledObject?> TryGetVisiblePrecompiled(long objectId)
    {
        var precompiled =
            await database.PrecompiledObjects.FirstOrDefaultAsync(p => p.Id == objectId && !p.Deleted);

        if (precompiled == null)
            return null;

        if (!precompiled.Public)
        {
            if (!HttpContext.HasAuthenticatedUserWithGroup(GroupType.Developer, AuthenticationScopeRestriction.None))
                return null;
        }

        return precompiled;
    }

    [NonAction]
    private Task<bool> CheckIfVersionAlreadyExists(PrecompiledObject precompiled, PrecompiledObjectVersionDTO request)
    {
        return database.PrecompiledObjectVersions.AnyAsync(v =>
            v.OwnedById == precompiled.Id && v.Version == request.Version && v.Platform == request.Platform &&
            v.Tags == request.Tags);
    }

    [NonAction]
    private async Task<PrecompiledObjectVersion?> GetVersionForDownload(long objectId, string version, int platform,
        int tags)
    {
        if (!remoteStorage.Configured)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status500InternalServerError,
                Value = "Remote storage is not configured",
            };
        }

        var parsedPlatform = (PackagePlatform)platform;
        var parsedTags = (PrecompiledTag)tags;

        var precompiled = await TryGetVisiblePrecompiled(objectId);

        if (precompiled == null)
            return null;

        return await database.PrecompiledObjectVersions.Include(v => v.StoredInItem)
            .ThenInclude(i => i.StorageItemVersions).FirstOrDefaultAsync(
                v => v.OwnedById == precompiled.Id && v.Version == version && v.Platform == parsedPlatform &&
                    v.Tags == parsedTags);
    }

    [NonAction]
    private async Task<string> GetDownloadLink(PrecompiledObjectVersion objectVersion)
    {
        if (!remoteStorage.Configured)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status500InternalServerError,
                Value = "Remote storage is not configured",
            };
        }

        var versionToDownload =
            objectVersion.StoredInItem.StorageItemVersions.OrderByDescending(v => v.Version).First();

        var file = await database.StorageFiles.FindAsync(versionToDownload.StorageFileId);

        if (file == null)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status500InternalServerError,
                Value = "Internal file is missing for this version",
            };
        }

        if (file.Uploading)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status500InternalServerError,
                Value = "Internal file is not uploaded for this version",
            };
        }

        if (objectVersion.LastDownload == null ||
            DateTime.UtcNow - objectVersion.LastDownload.Value > TimeSpan.FromSeconds(60))
        {
            // Bump the last download time
            objectVersion.LastDownload = DateTime.UtcNow;
            await database.SaveChangesAsync();
        }

        return downloadUrls.CreateDownloadFor(file, AppInfo.RemoteStorageDownloadExpireTime);
    }
}
