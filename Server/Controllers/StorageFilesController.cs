namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3.Model;
using Authorization;
using BlazorPagination;
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
using Shared.Converters;
using Shared.Forms;
using Shared.Models;
using Shared.Utilities;
using Utilities;

[ApiController]
[Route("api/v1/Files")]
public class StorageFilesController : Controller
{
    private const string FileUploadProtectionPurposeString = "StorageFilesController.Upload.v1";
    private const string FileUploadChunkProtectionPurposeString = "StorageFilesController.UploadChunk.v1";

    private readonly ILogger<StorageFilesController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly GeneralRemoteStorage remoteStorage;
    private readonly IBackgroundJobClient jobClient;
    private readonly ITimeLimitedDataProtector dataProtector;
    private readonly ITimeLimitedDataProtector chunkDataProtector;

    public StorageFilesController(ILogger<StorageFilesController> logger, NotificationsEnabledDb database,
        GeneralRemoteStorage remoteStorage, IDataProtectionProvider dataProtectionProvider,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.remoteStorage = remoteStorage;
        this.jobClient = jobClient;

        dataProtector = dataProtectionProvider.CreateProtector(FileUploadProtectionPurposeString)
            .ToTimeLimitedDataProtector();
        chunkDataProtector = dataProtectionProvider.CreateProtector(FileUploadChunkProtectionPurposeString)
            .ToTimeLimitedDataProtector();
    }

    [HttpGet("itemFromPath")]
    public async Task<ActionResult<PathParseResult>> ParsePath([MaxLength(500)] string? path)
    {
        var user = HttpContext.AuthenticatedUser();

        if (user != null && HttpContext.AuthenticatedUserRestriction() != AuthenticationScopeRestriction.None)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status403Forbidden,
                Value = "You authentication method has incorrect access restriction for this endpoint",
            };
        }

        // Return root folder if the path is empty
        if (string.IsNullOrEmpty(path))
        {
            return new PathParseResult
            {
                FinalItem = null,
            };
        }

        var pathParts = path.Split('/');

        StorageItem? currentItem = null;
        StorageItem? parentItem = null;

        foreach (var part in pathParts)
        {
            // Skip empty parts to support starting with a slash or having multiple in a row
            if (string.IsNullOrEmpty(part))
                continue;

            // If we have already found a file, then further path parts are invalid
            if (currentItem?.Ftype == FileType.File)
                return BadRequest("Detected further path components after a file was found");

            var currentId = currentItem?.Id;
            var nextItem =
                await database.StorageItems.FirstOrDefaultAsync(i => i.ParentId == currentId && i.Name == part);

            if (nextItem == null || !nextItem.IsReadableBy(user))
            {
                return NotFound(
                    $"Path part \"{part}\" doesn't exist or you don't have permission to view it. " +
                    "Logging in may help.");
            }

            // Store the parent item so that when a file's path is parsed also the parent folder info can be found
            parentItem = currentItem;
            currentItem = nextItem;
        }

        return new PathParseResult
        {
            ParentFolder = parentItem?.GetDTO(),
            FinalItem = currentItem?.GetDTO(),
        };
    }

    [HttpGet("folderContents")]
    public async Task<ActionResult<PagedResult<StorageItemInfo>>> GetFolderContents([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 500)] int pageSize, long? parentId = null)
    {
        // NOTE: we don't verify the parent accesses recursively, so for example if a folder has public read, but
        // it is contained in a private folder, the contents can be read through this if the parent id is known.

        StorageItem? item = null;
        if (parentId != null)
        {
            item = await FindAndCheckAccess(parentId.Value);

            if (item == null)
                return NotFound("Folder doesn't exist, or you don't have access to it");
        }
        else
        {
            // Everyone has read access to the root folder
        }

        IAsyncEnumerable<StorageItem> query;

        try
        {
            query = database.StorageItems.Where(i => i.ParentId == parentId).ToAsyncEnumerable()
                .OrderByDescending(p => p.Ftype).ThenBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        // Filter out objects not readable by current user
        // NOTE: that as a special case folder owner always sees all items, even if their contents are not readable
        // (this is to make things consistent with the notifications hub)
        var reader = HttpContext.AuthenticatedUser();

        if (item == null || item.OwnerId == null || item.OwnerId != reader?.Id)
        {
            query = query.Where(i => i.IsReadableBy(reader));
        }

        // And then return the contents of this folder to the requester
        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetInfo());
    }

    [HttpPost("createFolder")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
    public async Task<IActionResult> CreateFolder([Required] [FromBody] CreateFolderForm request)
    {
        if (!CheckNewItemName(request.Name, out var badRequest))
            return badRequest!;

        if (request.ReadAccess == FileAccess.Nobody || request.WriteAccess == FileAccess.Nobody)
            return BadRequest("Only system can create system readable/writable folders");

        var user = HttpContext.AuthenticatedUserOrThrow();

        // Check write access
        StorageItem? parentFolder = null;

        if (request.ParentFolder != null)
        {
            parentFolder = await database.StorageItems.FirstOrDefaultAsync(i =>
                i.Ftype == FileType.Folder && i.Id == request.ParentFolder.Value);

            if (parentFolder == null)
                return NotFound("Parent folder doesn't exist");
        }

        // Write access
        if (parentFolder == null)
        {
            // Only admin can write to root folder
            if (!user.HasAccessLevel(UserAccessLevel.Admin))
                return this.WorkingForbid("Only admins can write to root folder");
        }
        else
        {
            if (!parentFolder.IsWritableBy(user))
                return this.WorkingForbid("You don't have write access to the parent folder");
        }

        // Check for duplicate name
        var parentId = parentFolder?.Id;

        if (await database.StorageItems.FirstOrDefaultAsync(i =>
                i.ParentId == parentId && i.Name == request.Name) != null)
        {
            return BadRequest("Item with that name already exists in the parent folder");
        }

        // Folder is fine to be created
        await database.LogEntries.AddAsync(new LogEntry
        {
            Message = $"New folder \"{request.Name}\" created with owner",
            TargetUserId = user.Id,
        });

        var newFolder = new StorageItem
        {
            Name = request.Name,
            Ftype = FileType.Folder,
            ReadAccess = request.ReadAccess,
            WriteAccess = request.WriteAccess,
            AllowParentless = parentId == null,
            ParentId = parentId,
            OwnerId = user.Id,
        };

        await database.StorageItems.AddAsync(newFolder);

        await database.SaveChangesAsync();

        logger.LogInformation("New folder \"{Name}\" with read: {ReadAccess} and write access: " +
            "{WriteAccess}, created by: {Email}", newFolder.Name,
            newFolder.ReadAccess.ToUserReadableString(), newFolder.WriteAccess.ToUserReadableString(), user.Email);

        // Need to queue a job to calculate the folder size
        jobClient.Enqueue<CountFolderItemsJob>(x => x.Execute(newFolder.Id, CancellationToken.None));

        return Ok($"New folder with id {newFolder.Id} created");
    }

    [HttpGet("{id:long}/versions")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
    public async Task<ActionResult<PagedResult<StorageItemVersionInfo>>> GetVersions([Required] long id,
        [Required] string sortColumn, [Required] SortDirection sortDirection,
        [Required] [Range(1, int.MaxValue)] int page, [Required] [Range(1, 100)] int pageSize)
    {
        StorageItem? item = await FindAndCheckAccess(id);
        if (item == null)
            return NotFound();

        IQueryable<StorageItemVersion> query;

        try
        {
            query = database.StorageItemVersions.Include(v => v.StorageFile)
                .Where(v => v.StorageItemId == item.Id).OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        // And then return the contents of this folder to the requester
        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetInfo());
    }

    [HttpPut("{id:long}/edit")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
    public async Task<IActionResult> EditItem([Required] long id,
        [Required] [FromBody] StorageItemDTO newData)
    {
        StorageItem? item = await FindAndCheckAccess(id);
        if (item == null)
            return NotFound();

        if (item.Special)
            return BadRequest("Special items can't be edited");

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (item.WriteAccess == FileAccess.Nobody)
            return BadRequest("This item is not writable");

        if (item.OwnerId != user.Id && !user.HasAccessLevel(UserAccessLevel.Admin))
            return BadRequest("Only item owners and admins can edit them");

        if (newData.ReadAccess == FileAccess.Nobody || newData.WriteAccess == FileAccess.Nobody)
            return BadRequest("Only system can set system readable/writable status");

        if (!CheckNewItemName(newData.Name, out var badRequest))
            return badRequest!;

        item.Name = newData.Name;
        item.ReadAccess = newData.ReadAccess;
        item.WriteAccess = newData.WriteAccess;

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message =
                $"StorageItem {item.Id} edited, new name: \"{item.Name}\", accesses: {item.ReadAccess}, " +
                $"{item.WriteAccess}",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        return Ok();
    }

    [NonAction]
    private bool CheckNewItemName(string name, out ActionResult? badRequest)
    {
        // Purely numeric names (that are short) or starting with '@' are disallowed
        // TODO: would be nice to do this validation also on the client side form
        if (name.StartsWith('@') || (name.Length <= 5 && int.TryParse(name, out int _)))
        {
            badRequest = BadRequest("You specified a disallowed folder name");
            return false;
        }

        badRequest = null;
        return true;
    }

    [HttpPost("startUpload")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
    public async Task<ActionResult<UploadFileResponse>> StartFileUpload(
        [Required] [FromBody] UploadFileRequestForm request)
    {
        if (!CheckNewItemName(request.Name, out var badRequest))
            return badRequest!;

        if (!remoteStorage.Configured)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status500InternalServerError,
                Value = "Remote storage is not configured on the server",
            };
        }

        // Disallow extensions with uppercase letters
        if (PathParser.IsExtensionUppercase(request.Name))
            return BadRequest("File extension can't contain uppercase characters");

        // TODO: maybe in the future we'll want to allow anonymous uploads to certain folders
        var user = HttpContext.AuthenticatedUserOrThrow();

        // Check write access
        StorageItem? parentFolder = null;

        if (request.ParentFolder != null)
        {
            parentFolder = await database.StorageItems.FirstOrDefaultAsync(i =>
                i.Ftype == FileType.Folder && i.Id == request.ParentFolder.Value);

            if (parentFolder == null)
                return NotFound("Parent folder doesn't exist");
        }

        // Check if the item already exists (a new version is being uploaded)
        var parentId = parentFolder?.Id;
        var existingItem =
            await database.StorageItems.FirstOrDefaultAsync(i => i.ParentId == parentId && i.Name == request.Name);

        if (existingItem != null)
        {
            // New version of an existing item. User needs at least read access to the folder and
            // Root folder is publicly readable so that doesn't need to be checked here
            if (parentFolder?.IsReadableBy(user) == false)
                return this.WorkingForbid("You don't have read access to the folder");

            // Disallow file uploads to a folder item
            if (existingItem.Ftype != FileType.File)
                return BadRequest("Can't upload a new file version to an item that is not a file");
        }
        else
        {
            // Write access required to make a new item
            if (parentFolder == null)
            {
                if (!user.HasAccessLevel(UserAccessLevel.Admin))
                    return this.WorkingForbid("Only admins can write to root folder");
            }
            else
            {
                if (!parentFolder.IsWritableBy(user))
                    return this.WorkingForbid("You don't have write access to the folder");
            }
        }

        if (existingItem == null)
        {
            existingItem = new StorageItem
            {
                Name = request.Name,
                Ftype = FileType.File,
                ReadAccess = request.ReadAccess,
                WriteAccess = request.WriteAccess,
                AllowParentless = parentId == null,
                Parent = parentFolder,
                OwnerId = user.Id,
            };

            await database.StorageItems.AddAsync(existingItem);
        }

        var version = await existingItem.CreateNextVersion(database);
        var file = await version.CreateStorageFile(database,
            DateTime.UtcNow + AppInfo.RemoteStorageUploadExpireTime, request.Size);

        string? uploadUrl = null;
        MultipartFileUpload? multipart = null;
        long? multipartId = null;
        string? uploadId = null;

        if (request.Size >= AppInfo.FileSizeBeforeMultipartUpload)
        {
            // Multipart upload is recommended for large files, as large files are hard to make go through
            // in a reasonable time with a single PUT request
            try
            {
                uploadId = await remoteStorage.CreateMultipartUpload(file.UploadPath, request.MimeType);
                if (uploadId == null)
                    throw new Exception("returned uploadId is null");
            }
            catch (Exception e)
            {
                logger.LogError("Failed to create multipart upload: {@E}", e);
                return Problem("Failed to create a new multipart upload");
            }

            var chunks = ComputeChunksForFile(request.Size).ToList();
            var initialChunksToUpload = AddUploadUrlsToChunks(chunks.Take(AppInfo.MultipartSimultaneousUploads *
                    AppInfo.MultipartUploadPartsToReturnInSingleCall), file.UploadPath, uploadId,
                AppInfo.RemoteStorageUploadExpireTime).ToList();

            var multipartModel = new InProgressMultipartUpload
            {
                UploadId = uploadId,
                Path = file.UploadPath,
                NextChunkIndex = initialChunksToUpload.Count,
            };

            await database.InProgressMultipartUploads.AddAsync(multipartModel);
            await database.SaveChangesAsync();

            multipartId = multipartModel.Id;

            var chunkToken = new ChunkRetrieveToken(multipartModel.Id, file.Id, uploadId);

            var chunkTokenStr = JsonSerializer.Serialize(chunkToken);

            multipart = new MultipartFileUpload
            {
                ChunkRetrieveToken =
                    chunkDataProtector.Protect(chunkTokenStr, AppInfo.MultipartUploadTotalAllowedTime),
                TotalChunks = chunks.Count,
                NextChunks = initialChunksToUpload,
            };
        }
        else
        {
            // Normal upload (in a single PUT request)
            await database.SaveChangesAsync();

            uploadUrl = remoteStorage.CreatePresignedUploadURL(file.UploadPath,
                AppInfo.RemoteStorageUploadExpireTime);
        }

        // Need to queue a job to calculate the parent folder size
        if (parentId != null)
        {
            jobClient.Enqueue<CountFolderItemsJob>(x => x.Execute(parentId.Value,
                CancellationToken.None));
        }

        if (uploadId != null)
        {
            jobClient.Schedule<DeleteNonFinishedMultipartUploadJob>(x => x.Execute(uploadId,
                CancellationToken.None), AppInfo.MultipartUploadTotalAllowedTime * 2);
        }

        // TODO: queue a job to delete the version / UploadPath after a few hours if the upload fails

        var token = new UploadVerifyToken
        {
            TargetStorageItem = existingItem.Id,
            TargetStorageItemVersion = version.Id,
            MultipartId = multipartId,
        };

        var tokenStr = JsonSerializer.Serialize(token);

        return new UploadFileResponse
        {
            UploadURL = uploadUrl,
            Multipart = multipart,
            TargetStorageItem = existingItem.Id,
            TargetStorageItemVersion = version.Id,
            UploadVerifyToken = dataProtector.Protect(tokenStr,
                multipart == null ?
                    AppInfo.RemoteStorageUploadExpireTime :
                    AppInfo.MultipartUploadTotalAllowedTime),
        };
    }

    [HttpPost("moreChunks")]
    public async Task<ActionResult<MultipartFileUpload>> GetMoreMultipartChunks(
        [Required] [FromBody] MoreChunksRequestForm request)
    {
        // Verify token first as there is no other protection on this endpoint
        ChunkRetrieveToken verifiedToken;

        try
        {
            verifiedToken = JsonSerializer.Deserialize<ChunkRetrieveToken>(
                chunkDataProtector.Unprotect(request.Token)) ?? throw new NullDecodedJsonException();
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to verify more chunks token: {@E}", e);
            return BadRequest("Invalid more chunks token");
        }

        var upload = await database.InProgressMultipartUploads.FindAsync(verifiedToken.MultipartId);
        var file = await database.StorageFiles.FindAsync(verifiedToken.TargetStorageFile);

        if (upload == null || file == null || upload.Finished ||
            !SecurityHelpers.SlowEquals(upload.UploadId, verifiedToken.UploadId))
        {
            return BadRequest("Invalid specified file or multipart data in chunks token");
        }

        List<MultipartFileUpload.FileChunk>? chunks;
        if (file.Size != null)
        {
            chunks = AddUploadUrlsToChunks(
                ComputeChunksForFile(file.Size.Value).Skip(upload.NextChunkIndex)
                    .Take(AppInfo.MultipartUploadPartsToReturnInSingleCall), upload.Path, upload.UploadId,
                AppInfo.RemoteStorageUploadExpireTime).ToList();
        }
        else
        {
            return Problem("File in database is missing size");
        }

        if (chunks.Count < 1)
        {
            chunks = null;
        }
        else
        {
            upload.BumpUpdatedAt();
            upload.NextChunkIndex += chunks.Count;
            await database.SaveChangesAsync();
        }

        return new MultipartFileUpload { NextChunks = chunks };
    }

    [HttpPost("finishUpload")]
    public async Task<IActionResult> ReportFinishedUpload([Required] [FromBody] UploadFileResponse finishedUpload)
    {
        // Verify token first as there is no other protection on this endpoint
        UploadVerifyToken verifiedToken;

        try
        {
            verifiedToken = JsonSerializer.Deserialize<UploadVerifyToken>(
                dataProtector.Unprotect(finishedUpload.UploadVerifyToken)) ?? throw new NullDecodedJsonException();
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to verify general file upload token: {@E}", e);
            return BadRequest("Invalid upload token");
        }

        // TODO: unify this with the debug symbol and devbuild uploading
        // remoteStorage.HandleFinishedUploadToken

        var item = await database.StorageItems.FindAsync(verifiedToken.TargetStorageItem);
        var version = await database.StorageItemVersions.Include(v => v.StorageFile)
            .FirstOrDefaultAsync(v => v.Id == verifiedToken.TargetStorageItemVersion);

        if (item == null || version == null || item.Ftype != FileType.File || version.StorageFile == null)
            return BadRequest("Invalid specified version or item in verify token");

        // Finish the upload if it is a multipart one
        if (verifiedToken.MultipartId.HasValue)
        {
            var upload = await database.InProgressMultipartUploads.FindAsync(verifiedToken.MultipartId);

            if (upload == null || (upload.Path != version.StorageFile.UploadPath &&
                    upload.Path != version.StorageFile.StoragePath))
            {
                return BadRequest("Could not find multipart upload data");
            }

            try
            {
                var parts = await remoteStorage.ListMultipartUploadParts(upload.Path, upload.UploadId);
                await remoteStorage.FinishMultipartUpload(upload.Path, upload.UploadId,
                    parts.Select(p => (PartETag)p).ToList());
            }
            catch (Exception e)
            {
                logger.LogWarning("Failed to complete multipart upload in remote storage: {@E}", e);
                return BadRequest("Failed to complete multipart upload in remote storage");
            }

            upload.Finished = true;
            upload.BumpUpdatedAt();
            logger.LogInformation("Finished multipart upload {UploadId}", upload.UploadId);
        }

        // Verify that the file is properly in remote storage and copy it to an unmodifiable path
        try
        {
            var actualSize = await remoteStorage.GetObjectSize(version.StorageFile.UploadPath);

            if (actualSize != version.StorageFile.Size)
            {
                // Try to delete the file to not leave it hanging around
                logger.LogWarning("Detected partial upload to remote storage, trying to delete partial upload");
                await remoteStorage.DeleteObject(version.StorageFile.UploadPath);
                logger.LogInformation("Partial upload deleted");
                return BadRequest(
                    "The upload was only partially successful. Remote storage file size doesn't match");
            }
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to check object size in storage: {@E}", e);
            return BadRequest("Failed to retrieve the object size");
        }

        try
        {
            // Move the uploaded file to a path the user can't anymore access to overwrite it
            // TODO: this move only works for up to 5GB files, so multipart uploads over that size *must* go directly
            // to the storage path
            await remoteStorage.MoveObject(version.StorageFile.UploadPath, version.StorageFile.StoragePath);

            // Check the stored file size once again
            var movedSize = await remoteStorage.GetObjectSize(version.StorageFile.StoragePath);

            if (movedSize != version.StorageFile.Size)
            {
                logger.LogWarning("File size after move doesn't match anymore");

                logger.LogInformation("Attempting to delete the copied invalid file");

                await remoteStorage.DeleteObject(version.StorageFile.StoragePath);

                return BadRequest(
                    "Error copying the uploaded file in remote storage (size changed)");
            }
        }
        catch (Exception e)
        {
            logger.LogError("Move to right path remote storage file failed: {@E}", e);
            return BadRequest("Error copying the uploaded file in remote storage");
        }

        // Then update the model state
        version.StorageFile.BumpUpdatedAt();
        version.BumpUpdatedAt();

        // Update StorageItem if the version is the latest
        if (version.Version >= await database.StorageItemVersions.Where(s => s.StorageItemId == item.Id)
                .MaxAsync(s => s.Version))
        {
            item.Size = version.StorageFile.Size;
            item.BumpUpdatedAt();
        }

        await remoteStorage.PerformFileUploadSuccessActions(version.StorageFile, database);
        await database.SaveChangesAsync();

        logger.LogInformation("StorageItem {Id} has now version {Version} uploaded", item.Id, version.Version);
        return Ok();
    }

    [NonAction]
    private async Task<StorageItem?> FindAndCheckAccess(long id, bool read = true)
    {
        var item = await database.StorageItems.FindAsync(id);

        if (item == null)
            return null;

        var user = HttpContext.AuthenticatedUser();

        if (user != null && HttpContext.AuthenticatedUserRestriction() != AuthenticationScopeRestriction.None)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status403Forbidden,
                Value = "You authentication method has incorrect access restriction for this endpoint",
            };
        }

        if (read)
        {
            if (!item.IsReadableBy(user))
                return null;
        }
        else
        {
            if (!item.IsWritableBy(user))
                return null;
        }

        return item;
    }

    [NonAction]
    private IEnumerable<MultipartFileUpload.FileChunk> ComputeChunksForFile(long fileSize)
    {
        if (fileSize < 1)
            throw new ArgumentException();

        int chunkNumber = 0;
        long chunkSize = GetChunkSize(fileSize);
        long offset = 0;

        while (fileSize > 0)
        {
            var chunk = new MultipartFileUpload.FileChunk
            {
                ChunkNumber = ++chunkNumber,
                Length = Math.Min(chunkSize, fileSize),
                Offset = offset,
            };
            yield return chunk;

            offset += chunk.Length;
            fileSize -= chunk.Length;
        }
    }

    [NonAction]
    private IEnumerable<MultipartFileUpload.FileChunk> AddUploadUrlsToChunks(
        IEnumerable<MultipartFileUpload.FileChunk> chunks, string path, string uploadId, TimeSpan expiresIn)
    {
        foreach (var chunk in chunks)
        {
            chunk.UploadURL = remoteStorage.CreatePresignedUploadURL(path, uploadId, chunk.ChunkNumber, expiresIn);
            yield return chunk;
        }
    }

    [NonAction]
    private long GetChunkSize(long fileSize)
    {
        if (fileSize <= AppInfo.MultipartUploadChunkSizeLargeThreshold)
        {
            return AppInfo.MultipartUploadChunkSize;
        }

        return AppInfo.MultipartUploadChunkSizeLarge;
    }

    private class UploadVerifyToken
    {
        [Required]
        public long TargetStorageItem { get; set; }

        [Required]
        public long TargetStorageItemVersion { get; set; }

        public long? MultipartId { get; set; }
    }

    private class ChunkRetrieveToken
    {
        public ChunkRetrieveToken(long multipartId, long targetStorageFile, string uploadId)
        {
            MultipartId = multipartId;
            TargetStorageFile = targetStorageFile;
            UploadId = uploadId;
        }

        [Required]
        public long MultipartId { get; set; }

        [Required]
        public long TargetStorageFile { get; set; }

        [Required]
        public string UploadId { get; set; }
    }
}
