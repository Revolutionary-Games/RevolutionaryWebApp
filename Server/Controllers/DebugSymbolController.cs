namespace RevolutionaryWebApp.Server.Controllers;

using System;
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
using Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class DebugSymbolController : Controller
{
    private const string SymbolUploadProtectionPurposeString = "DebugSymbol.Upload.v1";

    private readonly ILogger<DebugSymbolController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IGeneralRemoteStorage remoteStorage;
    private readonly IBackgroundJobClient jobClient;
    private readonly IDataProtector dataProtector;

    public DebugSymbolController(ILogger<DebugSymbolController> logger, NotificationsEnabledDb database,
        IGeneralRemoteStorage remoteStorage, IBackgroundJobClient jobClient,
        IDataProtectionProvider dataProtectionProvider)
    {
        this.logger = logger;
        this.database = database;
        this.remoteStorage = remoteStorage;
        this.jobClient = jobClient;
        dataProtector = dataProtectionProvider.CreateProtector(SymbolUploadProtectionPurposeString);
    }

    [HttpGet]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    public async Task<ActionResult<PagedResult<DebugSymbolDTO>>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<DebugSymbol> query;

        try
        {
            query = database.DebugSymbols.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpPost("offerSymbols")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    public async Task<ActionResult<DebugSymbolOfferResponse>> OfferSymbols(
        [Required] [FromBody] DebugSymbolOfferRequest request)
    {
        foreach (var symbolPath in request.SymbolPaths)
        {
            if (symbolPath.StartsWith("/"))
                return BadRequest("Symbol path should not start with '/'");
        }

        var existing = await database.DebugSymbols.Where(d => request.SymbolPaths.Contains(d.RelativePath))
            .Select(d => d.RelativePath).ToListAsync();

        return new DebugSymbolOfferResponse
            { Upload = request.SymbolPaths.Where(s => !existing.Contains(s)).ToList() };
    }

    [HttpPost("startUpload")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    public async Task<ActionResult<DebugSymbolUploadResult>> StartUpload(
        [Required] [FromBody] DebugSymbolUploadRequest request)
    {
        if (request.SymbolPath.Contains('\\'))
            return BadRequest("The path contains a Windows line separator");

        if (request.SymbolPath.StartsWith("/") || request.SymbolPath.Contains(".."))
            return BadRequest("The path must not start with a slash or contain two dots in a row");

        logger.LogInformation("Upload request for symbol: {SymbolPath}", request.SymbolPath);

        if (request.SymbolPath.Count(c => c == '/') < 2 || !request.SymbolPath.EndsWith(".sym"))
            return BadRequest("The path must contain at least two path separators and end in .sym");

        if (!remoteStorage.Configured)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status500InternalServerError,
                Value = "Remote storage is not configured",
            };
        }

        var folder = await StorageItem.GetSymbolsFolder(database);

        if (folder == null)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status500InternalServerError,
                Value = "Storage folder is missing",
            };
        }

        var user = HttpContext.AuthenticatedUser()!;

        var symbol = new DebugSymbol
        {
            // Start in non-active state until the upload is ready
            Active = false,
            Uploaded = false,
            Name = request.SymbolPath.Split("/").Last(),
            RelativePath = request.SymbolPath,
            CreatedById = user.Id,
            Size = request.Size,
        };

        var storageItem = new StorageItem
        {
            Name = symbol.StorageFileName,
            Parent = folder,
            Ftype = FileType.File,
            Special = true,
            ReadAccess = FileAccess.Developer,
            WriteAccess = FileAccess.Nobody,
        };

        symbol.StoredInItem = storageItem;

        await database.StorageItems.AddAsync(storageItem);
        await database.DebugSymbols.AddAsync(symbol);

        // This save will fail if duplicate upload was attempted
        await database.SaveChangesAsync();

        jobClient.Enqueue<CountFolderItemsJob>(x => x.Execute(folder.Id, CancellationToken.None));

        logger.LogInformation("New DebugSymbol ({Id}) \"{RelativePath}\" created by {Email}", symbol.Id,
            symbol.RelativePath, user.Email);

        // Create a version to upload to
        var version = await symbol.StoredInItem.CreateNextVersion(database, user);

        var file = await version.CreateStorageFile(database,
            DateTime.UtcNow + AppInfo.RemoteStorageUploadExpireTime,
            request.Size);

        if (request.Size != file.Size)
            throw new Exception("Logic error in StorageFile size setting");

        await database.SaveChangesAsync();

        logger.LogInformation("Upload of DebugSymbol {Id} starting from {RemoteIpAddress}",
            symbol.Id, HttpContext.Connection.RemoteIpAddress);

        jobClient.Schedule<DeleteDebugSymbolIfUploadFailedJob>(x => x.Execute(symbol.Id, CancellationToken.None),
            AppInfo.RemoteStorageUploadExpireTime * 2);

        return new DebugSymbolUploadResult
        {
            UploadUrl = remoteStorage.CreatePresignedUploadURL(file.UploadPath,
                AppInfo.RemoteStorageUploadExpireTime),
            VerifyToken = new StorageUploadVerifyToken(dataProtector, file.UploadPath, file.StoragePath,
                file.Size.Value, file.Id, symbol.Id, null, null).ToString(),
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

        if (decodedToken?.ParentId == null)
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

        // This is used in the token here to store the devbuild ID
        var symbol = await database.DebugSymbols.FindAsync(decodedToken.ParentId.Value);

        if (symbol == null)
            return BadRequest("No symbol found with the id in the token");

        // Mark it as ready to use for stackwalk operations happening in the future
        symbol.Active = true;
        symbol.Uploaded = true;
        symbol.BumpUpdatedAt();

        await remoteStorage.PerformFileUploadSuccessActions(file, database);
        await database.SaveChangesAsync();

        logger.LogInformation("DebugSymbol {Id} ({StoragePath}) is now uploaded", symbol.Id, file.StoragePath);

        return Ok();
    }

    [HttpPut("{id:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    public async Task<IActionResult> UpdateSymbol([Required] [FromBody] DebugSymbolDTO request)
    {
        var symbol = await database.DebugSymbols.FindAsync(request.Id);

        if (symbol == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(symbol, request);

        if (!changes)
            return Ok();

        symbol.BumpUpdatedAt();

        await database.ActionLogEntries.AddAsync(new ActionLogEntry($"DebugSymbol {symbol.Id} edited", description)
        {
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("DebugSymbol {Id} edited by {Email}, changes: {Description}", symbol.Id,
            user.Email, description);

        return Ok();
    }
}
