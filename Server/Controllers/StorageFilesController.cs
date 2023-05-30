namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3.Model;
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
using Shared.Forms;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Utilities;
using SharedBase.Utilities;
using Utilities;

[ApiController]
[Route("api/v1/Files")]
public class StorageFilesController : Controller
{
    private const string FileUploadProtectionPurposeString = "StorageFilesController.Upload.v1";
    private const string FileUploadChunkProtectionPurposeString = "StorageFilesController.UploadChunk.v1";

    private const string AlreadyInTargetFolderMessage = "Item is already in the target folder";

    private readonly ILogger<StorageFilesController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IGeneralRemoteStorage remoteStorage;
    private readonly IBackgroundJobClient jobClient;
    private readonly ITimeLimitedDataProtector dataProtector;
    private readonly ITimeLimitedDataProtector chunkDataProtector;

    public StorageFilesController(ILogger<StorageFilesController> logger, NotificationsEnabledDb database,
        IGeneralRemoteStorage remoteStorage, IDataProtectionProvider dataProtectionProvider,
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
        if (string.IsNullOrWhiteSpace(path))
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
            // Note that if the logic is modified here there's also very similar logic in
            // ParsePathAsFarAsPossible

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

        // Check read access if accessing non-root folder (everyone has read access to the root folder)
        var (item, actionResult) = await FindFolderWithReadAccess(parentId);
        if (actionResult != null)
            return actionResult;

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
        query = FilterNonReadableEntries(item, query);

        // And then return the contents of this folder to the requester
        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetInfo());
    }

    [HttpGet("folderFolders")]
    public async Task<ActionResult<PagedResult<StorageItemInfo>>> GetFolderFolders([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 500)] int pageSize, long? parentId = null)
    {
        var (item, actionResult) = await FindFolderWithReadAccess(parentId);
        if (actionResult != null)
            return actionResult;

        // This is async enumerable as IsReadableBy call can't be translated to the DB
        IAsyncEnumerable<StorageItem> query;

        try
        {
            query = database.StorageItems.Where(i => i.ParentId == parentId && i.Ftype == FileType.Folder)
                .OrderBy(sortColumn, sortDirection).ToAsyncEnumerable();
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        query = FilterNonReadableEntries(item, query);

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetInfo());
    }

    [ResponseCache(Duration = 1800)]
    [HttpGet("totalUsed")]
    public async Task<StorageUsageStats> CalculateTotalStorage()
    {
        // Fetch the IDs for the DevBuild folders to ignore their sizes
        long dehydratedId;
        long buildsId;
        try
        {
            dehydratedId = (await StorageItem.GetDehydratedFolder(database)).Id;
            buildsId = (await StorageItem.GetDevBuildBuildsFolder(database)).Id;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to get dehydrated data for total usage calculation");
            throw new HttpResponseException
            {
                Status = (int)HttpStatusCode.InternalServerError,
                Value = "Could not detect folders that should be excluded",
            };
        }

        // Calculate all version sizes while ignoring the devbuild files
        var bytes = await database.StorageItemVersions.Include(v => v.StorageFile).Include(v => v.StorageItem)
            .Where(v => v.StorageItem!.ParentId != dehydratedId && v.StorageItem!.ParentId != buildsId &&
                v.StorageFile!.Size != null && v.StorageFile!.Size > 0).SumAsync(v => v.StorageFile!.Size!.Value);

        return new StorageUsageStats(bytes);
    }

    [HttpPost("createFolder")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
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
            if (!user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
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
        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = $"New folder \"{request.Name}\" created",
            PerformedById = user.Id,
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

        // And parent folder also needs to calculate the new size
        if (parentFolder != null)
        {
            jobClient.Enqueue<CountFolderItemsJob>(x => x.Execute(parentFolder.Id, CancellationToken.None));
        }

        return Ok($"New folder with id {newFolder.Id} created");
    }

    [HttpGet("{id:long}/versions")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
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
                .Where(v => v.StorageItemId == item.Id);

            // If no write access, hide deleted versions
            if (!item.IsWritableBy(HttpContext.AuthenticatedUser()))
                query = query.Where(v => !v.Deleted);

            query = query.OrderBy(sortColumn, sortDirection);
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

    [HttpDelete("{id:long}/versions/{version:int}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<IActionResult> MarkVersionAsDeleted([Required] long id, [Required] int version)
    {
        StorageItem? item = await FindAndCheckAccess(id, false);
        if (item == null)
            return NotFound();

        var versionItem =
            await database.StorageItemVersions.FirstOrDefaultAsync(v => v.Version == version && v.StorageItem == item);

        if (versionItem == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        // Specific item states / version info prevents deleting
        // TODO: should special status prevent deleting a version?
        if (item.Important || versionItem.Protected)
            return BadRequest("This item or version is protected or important and can't be deleted");

        if (versionItem.Uploading)
            return BadRequest("Cannot delete an uploading version. Failed uploads will be automatically deleted");

        if (versionItem.Keep && item.OwnerId != user.Id && !user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
            return BadRequest("Only item owners and admins can delete versions marked as keep");

        // Skip if already deleted
        if (versionItem.Deleted)
            return Ok();

        // Disallow deleting the last version in a file
        if (!await database.StorageItemVersions.AnyAsync(v =>
                v.Version != versionItem.Version && v.StorageItem == item && !v.Deleted && !v.Uploading))
        {
            return BadRequest("Cannot delete the last version of a file, delete the entire file instead");
        }

        versionItem.Deleted = true;
        versionItem.BumpUpdatedAt();

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = $"StorageItem {item.Id} ({item.Name.Truncate()}) version {versionItem.Version} deleted",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{id:long}/versions/{version:int}/restore")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<IActionResult> RestoreVersion([Required] long id, [Required] int version)
    {
        StorageItem? item = await FindAndCheckAccess(id, false);
        if (item == null)
            return NotFound();

        if (item.Deleted)
            return BadRequest("Can't restore versions in a deleted item. Please restore the item itself first.");

        var versionItem =
            await database.StorageItemVersions.FirstOrDefaultAsync(v => v.Version == version && v.StorageItem == item);

        if (versionItem == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (item.Important)
            return BadRequest("The item this is in is marked as important, versions can't be restored");

        // Skip if already non-deleted
        if (!versionItem.Deleted)
            return Ok();

        versionItem.Deleted = false;
        versionItem.BumpUpdatedAt();

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = $"StorageItem {item.Id} version {versionItem.Version} restored",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{id:long}/versions/{version:int}/keep")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<IActionResult> MarkVersionKeep([Required] long id, [Required] int version)
    {
        StorageItem? item = await FindAndCheckAccess(id, false);
        if (item == null)
            return NotFound();

        if (item.Deleted)
        {
            return BadRequest(
                "Can't mark versions as keep within deleted items (the versions will be deleted when the item is " +
                "cleaned from the database)");
        }

        var versionItem =
            await database.StorageItemVersions.FirstOrDefaultAsync(v => v.Version == version && v.StorageItem == item);

        if (versionItem == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (versionItem.Deleted)
            return BadRequest("Deleted version can't be marked as kep");

        if (item.Special)
            return BadRequest("Special item can't have a version set as keep");

        if (versionItem.Keep)
            return Ok();

        versionItem.Keep = true;
        versionItem.BumpUpdatedAt();

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = $"StorageItem {item.Id} version {versionItem.Version} is now marked keep",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:long}/versions/{version:int}/keep")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<IActionResult> UnmarkVersionKeep([Required] long id, [Required] int version)
    {
        StorageItem? item = await FindAndCheckAccess(id, false);
        if (item == null)
            return NotFound();

        var versionItem =
            await database.StorageItemVersions.FirstOrDefaultAsync(v => v.Version == version && v.StorageItem == item);

        if (versionItem == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (item.OwnerId != user.Id && !user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
        {
            if (versionItem.UploadedById == null || versionItem.UploadedById != user.Id)
                return BadRequest("Only item owners, version uploaders and admins can unmark kept versions");
        }

        if (versionItem.Deleted)
            return BadRequest("Deleted version can't have their kept marking change");

        if (item.Special)
            return BadRequest("Special item can't have a version unset as keep");

        if (!versionItem.Keep)
            return Ok();

        versionItem.Keep = false;
        versionItem.BumpUpdatedAt();

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = $"StorageItem {item.Id} version {versionItem.Version} is no longer marked keep",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        return Ok();
    }

    [HttpPut("{id:long}/edit")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<IActionResult> EditItem([Required] long id,
        [Required] [FromBody] StorageItemDTO newData)
    {
        StorageItem? item = await FindAndCheckAccess(id, false);
        if (item == null)
            return NotFound();

        if (item.Deleted)
            return BadRequest("Deleted items can't be edited");

        if (item.Special)
            return BadRequest("Special items can't be edited");

        if (item.ModificationLocked)
            return BadRequest("Item info is set as non-modifiable");

        if (item.WriteAccess == FileAccess.Nobody)
            return BadRequest("This item is not writable");

        var user = HttpContext.AuthenticatedUserOrThrow();

        // When editing access only the owner can do so
        if (item.WriteAccess != newData.WriteAccess || item.ReadAccess != newData.ReadAccess)
        {
            if (item.OwnerId != user.Id && !user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
                return BadRequest("Only item owners and admins can edit item access");
        }

        if (newData.ReadAccess == FileAccess.Nobody || newData.WriteAccess == FileAccess.Nobody)
            return BadRequest("Only system can set system readable/writable status");

        if (item.WriteAccess == newData.WriteAccess && item.ReadAccess == newData.ReadAccess &&
            item.Name == newData.Name)
        {
            // No changes
            return Ok();
        }

        if (!CheckNewItemName(newData.Name, out var badRequest))
            return badRequest!;

        // Check the new name doesn't conflict
        if (item.Name != newData.Name)
        {
            var duplicate = await database.StorageItems.FirstOrDefaultAsync(i =>
                i.Name == newData.Name && i.ParentId == item.ParentId);

            if (duplicate != null)
            {
                return BadRequest("The new name is already in use, please select a unique name in the folder");
            }
        }

        item.Name = newData.Name;
        item.ReadAccess = newData.ReadAccess;
        item.WriteAccess = newData.WriteAccess;

        item.LastModifiedById = user.Id;
        item.BumpUpdatedAt();

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

    [HttpPost("{id:long}/lock")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<IActionResult> LockItem([Required] long id)
    {
        StorageItem? item = await FindAndCheckAccess(id, false);
        if (item == null)
            return NotFound();

        if (item.Deleted)
            return BadRequest("Can't lock a deleted item");

        if (item.Special)
            return BadRequest("Special items can't be edited");

        if (item.WriteAccess == FileAccess.Nobody)
            return BadRequest("This item is not writable");

        var user = HttpContext.AuthenticatedUserOrThrow();

        // Only owner can change lock status
        if (item.OwnerId != user.Id && !user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
            return BadRequest("Only item owners and admins can change locked status");

        // Exit if already in right status
        if (item.ModificationLocked)
            return Ok();

        item.ModificationLocked = true;

        item.LastModifiedById = user.Id;
        item.BumpUpdatedAt();

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = $"StorageItem {item.Id} is now in modification locked status",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:long}/lock")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<IActionResult> RemoveItemLock([Required] long id)
    {
        StorageItem? item = await FindAndCheckAccess(id, false);
        if (item == null)
            return NotFound();

        if (item.Special)
            return BadRequest("Special items can't be edited");

        if (item.WriteAccess == FileAccess.Nobody)
            return BadRequest("This item is not writable");

        var user = HttpContext.AuthenticatedUserOrThrow();

        // Only owner can change lock status
        if (item.OwnerId != user.Id && !user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
            return BadRequest("Only item owners and admins can change locked status");

        // Exit if already in right status
        if (!item.ModificationLocked)
            return Ok();

        item.ModificationLocked = false;

        item.LastModifiedById = user.Id;
        item.BumpUpdatedAt();

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = $"StorageItem {item.Id} is no longer in modification locked status",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{id:long}/important")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<IActionResult> MarkImportant([Required] long id)
    {
        StorageItem? item = await FindAndCheckAccess(id, false);
        if (item == null)
            return NotFound();

        if (item.Deleted)
            return BadRequest("Can't mark a deleted item as important");

        if (item.Special)
            return BadRequest("Special items can't be edited");

        if (item.WriteAccess == FileAccess.Nobody)
            return BadRequest("This item is not writable");

        var user = HttpContext.AuthenticatedUserOrThrow();

        // Only owner can change important status
        if (item.OwnerId != user.Id && !user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
            return BadRequest("Only item owners and admins can change important status");

        // For now only files can be made important
        if (item.Ftype != FileType.File)
            return BadRequest("Only files can be marked as important");

        // Exit if already in right status
        if (item.Important)
            return Ok();

        item.Important = true;

        item.LastModifiedById = user.Id;
        item.BumpUpdatedAt();

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = $"StorageItem {item.Id} is now important",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:long}/important")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<IActionResult> RemoveImportantStatus([Required] long id)
    {
        StorageItem? item = await FindAndCheckAccess(id, false);
        if (item == null)
            return NotFound();

        if (item.Deleted)
            return BadRequest("Can't remove important status from a deleted item");

        if (item.Special)
            return BadRequest("Special items can't be edited");

        if (item.WriteAccess == FileAccess.Nobody)
            return BadRequest("This item is not writable");

        var user = HttpContext.AuthenticatedUserOrThrow();

        // Only owner can change important status
        if (item.OwnerId != user.Id && !user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
            return BadRequest("Only item owners and admins can change important status");

        // Exit if already in right status
        if (!item.Important)
            return Ok();

        item.Important = false;

        item.LastModifiedById = user.Id;
        item.BumpUpdatedAt();

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = $"StorageItem {item.Id} is no longer marked important",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<IActionResult> DeleteOrTrash([Required] long id)
    {
        StorageItem? item = await FindAndCheckAccess(id, false, true);
        if (item == null)
            return NotFound();

        if (item.Special)
            return BadRequest("Special items can't be deleted");

        if (item.ModificationLocked)
            return BadRequest("Item with properties lock on can't be deleted");

        if (item.Important)
            return BadRequest("Important items can't be deleted");

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (item.Parent == null)
        {
            // Only admins can delete top level stuff
            if (!user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
                return this.WorkingForbid("Only admins can delete top level items");
        }
        else if (!item.Parent.IsWritableBy(user))
        {
            return this.WorkingForbid("You don't have write access to this item's parent folder");
        }

        // Folders are immediately deleted, but only if they are empty
        if (item.Ftype == FileType.Folder)
        {
            if (await database.StorageItems.AnyAsync(i => i.ParentId == item.Id))
            {
                return BadRequest(
                    "Only empty folders can be deleted. Please delete all child items first and try again.");
            }

            database.StorageItems.Remove(item);

            await database.ActionLogEntries.AddAsync(new ActionLogEntry
            {
                Message = $"Storage folder {item.Id} is now permanently deleted",
                PerformedById = user.Id,
            });

            logger.LogInformation("Storage folder {Id} ({Name}) permanently deleted by {Email}", item.Id, item.Name,
                user.Email);

            await database.SaveChangesAsync();

            // Recount items in the parent folder
            if (item.ParentId != null)
                jobClient.Enqueue<CountFolderItemsJob>(x => x.Execute(item.ParentId.Value, CancellationToken.None));

            return Ok();
        }

        if (item.Ftype == FileType.File)
        {
            if (item.Deleted)
                return BadRequest("Item is already deleted");

            var trash = await StorageItem.GetTrashFolder(database);

            if (item.ParentId == trash.Id)
                return BadRequest("Item is already in trash");

            await MoveFileToTrash(item, trash, user);
            return Ok();
        }

        return Problem("Unknown item type to delete");
    }

    [HttpGet("{id:long}/deleteStatus")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<ActionResult<StorageItemDeleteInfoDTO>> GetDeleteStatus([Required] long id)
    {
        StorageItem? item = await FindAndCheckAccess(id, true, false, true);
        if (item == null)
            return NotFound();

        if (!item.Deleted)
            return NotFound();

        return item.DeleteInfo?.GetDTO() ?? throw new Exception("Missing deleted info");
    }

    [HttpPost("{id:long}/restore")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<IActionResult> RestoreFile([Required] long id, [FromForm] string? customPath)
    {
        bool usesCustomPath = true;

        if (string.IsNullOrWhiteSpace(customPath) || customPath == "/")
        {
            customPath = null;
            usesCustomPath = false;
        }

        var item = await database.StorageItems.Include(i => i.DeleteInfo).ThenInclude(d => d!.OriginalFolderOwner)
            .Include(i => i.Parent)
            .FirstOrDefaultAsync(i => i.Id == id);
        var trash = await StorageItem.GetTrashFolder(database);

        if (item == null)
            return NotFound();

        // Can't restore an item that is not deleted
        if (!item.Deleted)
            return NotFound();

        if (item.DeleteInfo == null)
            throw new Exception("Delete info doesn't exist for a deleted file");

        var user = HttpContext.AuthenticatedUserOrThrow();

        // Item can only be undeleted by having read access (this is overridden to just file owner), being admin
        // or being the user who deleted the item
        if (!item.IsReadableBy(user) && !user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin) &&
            user.Id != item.DeleteInfo.DeletedById)
        {
            // TODO: should this require not found if no read access?

            return this.WorkingForbid("You lack the permissions required to restore this file");
        }

        if (trash.Id != item.ParentId)
            throw new Exception("Deleted file is not in the trash folder");

        if (item.Special)
            return BadRequest("Special item can't be restored by a user");

        // Currently only files are soft deleted
        if (item.Ftype != FileType.File)
            return Problem("Unknown item type category to restore");

        // File is good to be restored

        var originalPathParts = item.DeleteInfo.OriginalFolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Restore the original name in case the file was renamed when moved to trash
        item.Name = originalPathParts.Last();

        // figure out where to put it
        var pathToRestoreAt = customPath ?? string.Join('/', originalPathParts.Take(originalPathParts.Length - 1));

        // This fails with an exception if the move is not valid
        var restoredPath = await MoveFile(item, pathToRestoreAt, !usesCustomPath ? item.DeleteInfo : null, user, true);

        // Due to tests having in-memory data we do this modification only after the move has succeeded
        // Restore original file status

        item.Deleted = false;
        item.LastModifiedById = user.Id;
        item.WriteAccess = item.DeleteInfo.OriginalWriteAccess;
        item.ReadAccess = item.DeleteInfo.OriginalReadAccess;

        database.StorageItemDeleteInfos.Remove(item.DeleteInfo);
        item.DeleteInfo = null;

        item.BumpUpdatedAt();

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = $"StorageItem {item.Id} ({item.Name.Truncate()}) restored from trash",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Restored StorageItem {Id} ({Name}) to {RestoredPath} by {Email}", item.Id, item.Name,
            restoredPath, user.Email);

        return Ok(restoredPath);
    }

    [HttpPost("{id:long}/move")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<ActionResult> MoveItem([Required] long id, [FromBody] string? targetFolder)
    {
        if (string.IsNullOrWhiteSpace(targetFolder))
            targetFolder = string.Empty;

        StorageItem? item = await FindAndCheckAccess(id, false, true);
        if (item == null)
            return NotFound("Item to move not found or you don't have (write) access to it");

        if (item.Special)
            return BadRequest("Special items can't be moved");

        if (item.ModificationLocked)
            return BadRequest("Item with properties lock on can't be moved");

        if (item.Important)
            return BadRequest("Important items can't be moved");

        // Sanity check to make sure no one tries to move an item from the trash
        if (item.Deleted)
            return BadRequest("Deleted item can't be moved. Restore the item first.");

        var user = HttpContext.AuthenticatedUserOrThrow();

        var result = await MoveFile(item, targetFolder, null, user, false);

        if (result != AlreadyInTargetFolderMessage)
        {
            // We need to save the changes to make them stick
            await database.SaveChangesAsync();
        }

        return Ok(result);
    }

    [HttpPost("checkUploadDuplicate")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<ActionResult<FileDuplicateCheckResponse>> CheckDuplicateBeforeUpload(
        [Required] [FromBody] UploadFileRequestForm request)
    {
        // Don't bother checking with bad names
        if (string.IsNullOrEmpty(request.Name))
            return NoContent();

        var existingItem =
            await database.StorageItems.FirstOrDefaultAsync(i =>
                i.ParentId == request.ParentFolder && i.Name == request.Name);

        if (existingItem == null)
        {
            // No existing item, won't be a duplicate
            return NoContent();
        }

        var latestVersion = await existingItem.GetHighestUploadedVersion(database);

        if (latestVersion == null)
            return NoContent();

        // Now we can finally do the duplicate check
        if (latestVersion.StorageFile!.Size == request.Size)
        {
            // It is a duplicate
            return new FileDuplicateCheckResponse
            {
                PreviousVersionSize = latestVersion.StorageFile!.Size.Value,
                PreviousVersionTime = latestVersion.CreatedAt,
            };
        }

        return NoContent();
    }

    [HttpPost("startUpload")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
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
                if (!user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
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

        var version = await existingItem.CreateNextVersion(database, user);
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

            // We explicitly don't bump the version here as otherwise to be logically consistent we'd need to overwrite
            // the file last modifier which could lose info as for the version we already know who uploaded it, but in
            // addition we can keep the info on for example who renamed the file or moved it to trash
            // item.LastModifiedById = version.UploadedById;
            // item.BumpUpdatedAt();
        }

        await remoteStorage.PerformFileUploadSuccessActions(version.StorageFile, database);
        await database.SaveChangesAsync();

        logger.LogInformation("StorageItem {Id} has now version {Version} uploaded", item.Id, version.Version);
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

    [NonAction]
    private async Task<StorageItem?> FindAndCheckAccess(long id, bool read = true, bool loadParentFolder = false,
        bool loadDeleteInfo = false)
    {
        StorageItem? item;

        if (loadParentFolder && loadDeleteInfo)
        {
            item = await database.StorageItems.Include(i => i.Parent).Include(i => i.DeleteInfo)
                .FirstOrDefaultAsync(i => i.Id == id);
        }
        else if (loadDeleteInfo)
        {
            item = await database.StorageItems.Include(i => i.DeleteInfo).FirstOrDefaultAsync(i => i.Id == id);
        }
        else if (loadParentFolder)
        {
            item = await database.StorageItems.Include(i => i.Parent).FirstOrDefaultAsync(i => i.Id == id);
        }
        else
        {
            item = await database.StorageItems.FindAsync(id);
        }

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

    /// <summary>
    ///   Note that this doesn't verify read access to all parents, just the top parent.
    ///   See <see cref="GetFolderContents"/>.
    /// </summary>
    /// <param name="parentId">The parent folder id</param>
    /// <returns>
    ///   The item if good to access in a tuple, or a failed action result (if this is not null the find failed)
    /// </returns>
    [NonAction]
    private async Task<(StorageItem? Item, ActionResult? ActionResult)> FindFolderWithReadAccess(long? parentId)
    {
        if (parentId != null)
        {
            var item = await FindAndCheckAccess(parentId.Value);

            if (item == null)
            {
                return (null, NotFound("Folder doesn't exist, or you don't have access to it"));
            }

            return (item, null);
        }

        return (null, null);
    }

    [NonAction]
    private IAsyncEnumerable<StorageItem> FilterNonReadableEntries(StorageItem? folder,
        IAsyncEnumerable<StorageItem> query)
    {
        // NOTE: that as a special case folder owner always sees all items, even if their contents are not readable
        // (this is to make things consistent with the notifications hub)
        var reader = HttpContext.AuthenticatedUser();

        if (folder?.OwnerId == null || folder.OwnerId != reader?.Id)
        {
            query = query.Where(i => i.IsReadableBy(reader));
        }

        return query;
    }

    [NonAction]
    private async Task MoveFileToTrash(StorageItem item, StorageItem trashFolder, User user)
    {
        if (trashFolder.Name != "Trash" || !trashFolder.Special || trashFolder.Ftype != FileType.Folder)
            throw new InvalidOperationException("Wrong trash folder entry passed");

        if (item.ParentId == trashFolder.Id)
            throw new InvalidOperationException("File is already in the trash");

        var originalPath = await item.ComputeStoragePath(database);

        // Generate the delete info
        var deleteInfo = new StorageItemDeleteInfo(item, originalPath)
        {
            DeletedById = user.Id,
        };

        await database.StorageItemDeleteInfos.AddAsync(deleteInfo);

        // Modify the item to fit in the trash folder
        item.DeleteInfo = deleteInfo;
        item.Deleted = true;
        item.Parent = trashFolder;
        item.ParentId = trashFolder.Id;
        item.LastModifiedById = user.Id;
        item.WriteAccess = FileAccess.Nobody;
        item.ReadAccess = FileAccess.OwnerOrAdmin;

        // If the file has no owner, then set the deleter as the owner to make sure someone can see it
        // The special check here is just for extra safety, this should never trigger with a special
        // file, but in case this does we don't want to give ownership of system files to someone
        if (item.OwnerId == null && !item.Special)
        {
            item.OwnerId = user.Id;

            await database.ActionLogEntries.AddAsync(new ActionLogEntry
            {
                Message = $"StorageItem {item.Id} ownership was transferred from nobody due to move to trash",
                PerformedById = user.Id,
            });
        }

        item.BumpUpdatedAt();

        // If the filename is already in the trash we need to come up with a unique one
        if (await database.StorageItems.AnyAsync(i => i.ParentId == trashFolder.Id && i.Name == item.Name))
        {
            await item.MakeNameUniqueInFolder(database);
        }

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = $"StorageItem {item.Id} ({item.Name.Truncate()}) moved to trash",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        // Recount the items in the folder
        if (item.ParentId != null)
            jobClient.Enqueue<CountFolderItemsJob>(x => x.Execute(item.ParentId.Value, CancellationToken.None));
    }

    /// <summary>
    ///   Moves a file to a target folder if possible
    /// </summary>
    /// <param name="item">The moved item</param>
    /// <param name="targetFolder">Path to the target folder</param>
    /// <param name="targetFolderInfoToCreateWith">
    ///   If the target folder doesn't exist it will attempted to be created with this info
    /// </param>
    /// <param name="user">The acting user doing the move whose permissions matter</param>
    /// <param name="restore">If true then this acts in restore mode rather than just pure file move mode</param>
    /// <returns>The path the file is now at (including the file's own name)</returns>
    /// <exception cref="HttpResponseException">If the user does not have the right permissions</exception>
    [NonAction]
    private async Task<string> MoveFile(StorageItem item, string targetFolder,
        StorageItemDeleteInfo? targetFolderInfoToCreateWith, User user, bool restore)
    {
        // Find the target folder first to determine if it exists and the current user has access, or if it doesn't
        // and we can create a new folder based on targetFolderInfoToCreateWith (if that is not null)

        var parsed = await ParsePathAsFarAsPossible(targetFolder, user);

        StorageItem? finalFolderToMoveTo;
        string finalFolderPath;

        bool countTargetFolderSize = true;

        if (parsed.Parsed)
        {
            finalFolderToMoveTo = parsed.FullyParsed;
            finalFolderPath = targetFolder;

            // Check status of the parsed folder if it isn't the root folder
            if (parsed.FullyParsed != null)
            {
                if (parsed.FullyParsed.Ftype != FileType.Folder)
                {
                    throw new HttpResponseException
                    {
                        Status = (int)HttpStatusCode.Conflict,
                        Value = "Target path to move to leads a file, not a folder",
                    };
                }

                if (!parsed.FullyParsed.IsWritableBy(user))
                {
                    throw new HttpResponseException
                    {
                        Status = (int)HttpStatusCode.Conflict,
                        Value = "Target path to move to leads to a folder you do not have write access to",
                    };
                }
            }
        }
        else if (targetFolderInfoToCreateWith != null)
        {
            // Starting at the folder we left off in the parsing
            var createFoldersStart = parsed.ParsedUntil;
            var parsedPrefix = parsed.PartiallyParsedPath;

            var remainingPathToCreate = targetFolder;

            if (parsedPrefix != null)
            {
                // +1 is here to get past the last path separator character
                remainingPathToCreate = remainingPathToCreate.Substring(parsedPrefix.Length + 1);
            }

            // Create the folder with the info if possible
            finalFolderToMoveTo = await CreateMultipleFolders(createFoldersStart, remainingPathToCreate, user,
                targetFolderInfoToCreateWith.OriginalFolderReadAccess,
                targetFolderInfoToCreateWith.OriginalFolderWriteAccess,
                targetFolderInfoToCreateWith.OriginalFolderOwner ?? user,
                targetFolderInfoToCreateWith.OriginalFolderImportant,
                targetFolderInfoToCreateWith.OriginalFolderModificationLocked, restore);

            // Target folder is now created (as we didn't hit an exception)
            finalFolderPath = targetFolder;

            // The target folder is not saved in the DB yet, so we don't want to count its size (as we don't have an
            // id to give to the job). This is fine as the folder create already set the size to 1
            countTargetFolderSize = false;
        }
        else
        {
            throw new HttpResponseException
            {
                Status = (int)HttpStatusCode.Conflict,
                Value = "Target path to move to doesn't lead to a valid folder (or you don't have even read access)",
            };
        }

        if (item.Parent == finalFolderToMoveTo)
        {
            if (restore)
                throw new Exception("Cannot restore to the same folder item is already in");

            return AlreadyInTargetFolderMessage;
        }

        var nameConflict =
            await database.StorageItems.FirstOrDefaultAsync(i =>
                i.Parent == finalFolderToMoveTo && i.Name == item.Name);

        if (nameConflict != null)
        {
            throw new HttpResponseException
            {
                Status = (int)HttpStatusCode.Conflict,
                Value = $"Target folder already has an item named {nameConflict.Name}",
            };
        }

        if (finalFolderToMoveTo == null)
        {
            if (!user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
            {
                throw new HttpResponseException
                {
                    Status = (int)HttpStatusCode.Conflict,
                    Value = "Only admins can write to the root folder",
                };
            }

            if (restore)
            {
                await database.ActionLogEntries.AddAsync(new ActionLogEntry
                {
                    Message = $"StorageItem {item.Id} is now restored to root folder",
                    PerformedById = user.Id,
                });
            }
            else
            {
                await database.ActionLogEntries.AddAsync(new ActionLogEntry
                {
                    Message = $"StorageItem {item.Id} was moved to root folder",
                    PerformedById = user.Id,
                });
            }
        }
        else
        {
            if (restore)
            {
                await database.ActionLogEntries.AddAsync(new ActionLogEntry
                {
                    Message = $"StorageItem {item.Id} is now restored to folder {finalFolderToMoveTo.Id}",
                    PerformedById = user.Id,
                });
            }
            else
            {
                await database.ActionLogEntries.AddAsync(new ActionLogEntry
                {
                    Message = $"StorageItem {item.Id} was moved to folder {finalFolderToMoveTo.Id}",
                    PerformedById = user.Id,
                });
            }
        }

        if (finalFolderToMoveTo == item)
        {
            throw new HttpResponseException
            {
                Status = (int)HttpStatusCode.Conflict,
                Value = "Cannot move an item into itself",
            };
        }

        if (finalFolderToMoveTo != null && finalFolderToMoveTo.Special)
        {
            throw new HttpResponseException
            {
                Status = (int)HttpStatusCode.Conflict,
                Value = "Cannot move an item into a special folder",
            };
        }

        // Store the old path to allow users the chance to put things back when this is a move
        if (!restore)
        {
            var fullOldPath = await item.ComputeStoragePath(database);
            item.MovedFromLocation = fullOldPath;

            logger.LogInformation("StorageItem {Id} ({Name}) moved to {Id2} ({Name2}) from {FullOldPath} by {Email}",
                item.Id, item.Name, finalFolderToMoveTo?.Id, finalFolderToMoveTo?.Name, fullOldPath, user.Email);
        }

        // Queue job to recount the old folder contents
        if (item.ParentId != null)
        {
            jobClient.Schedule<CountFolderItemsJob>(x => x.Execute(item.ParentId.Value, CancellationToken.None),
                TimeSpan.FromSeconds(60));
        }

        item.Parent = finalFolderToMoveTo;
        item.LastModifiedById = user.Id;
        item.BumpUpdatedAt();

        // Queue job to recount the new folder contents
        if (item.Parent != null && countTargetFolderSize)
        {
            var parentId = item.Parent.Id;
            jobClient.Schedule<CountFolderItemsJob>(x => x.Execute(parentId, CancellationToken.None),
                TimeSpan.FromSeconds(60));
        }

        // This format should be fine also for root path items to start with "/"
        return $"{finalFolderPath}/{item.Name}";
    }

    [NonAction]
    private async Task<InternalPathParseResult> ParsePathAsFarAsPossible(string path, User? accessibleTo)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new InternalPathParseResult(null);

        var pathParts = path.Split('/');

        StorageItem? lastSuccessfullyParsed = null;

        bool partSeen = false;

        for (int i = 0; i < pathParts.Length; ++i)
        {
            // Note that if the logic is modified here there's also very similar logic in
            // ParsePath

            var part = pathParts[i];

            // Skip empty parts to support starting with a slash or having multiple in a row
            if (string.IsNullOrEmpty(part))
                continue;

            partSeen = true;

            // If we have already found a file, then further path parts are invalid
            if (lastSuccessfullyParsed?.Ftype == FileType.File)
                return new InternalPathParseResult(pathParts.Take(i), lastSuccessfullyParsed);

            var currentId = lastSuccessfullyParsed?.Id;
            var nextItem =
                await database.StorageItems.FirstOrDefaultAsync(s => s.ParentId == currentId && s.Name == part);

            if (nextItem == null || (accessibleTo != null && !nextItem.IsReadableBy(accessibleTo)))
            {
                // Non-existing / unreadable path
                return new InternalPathParseResult(pathParts.Take(i), lastSuccessfullyParsed);
            }

            // Do a bit of manual switching together here to keep the parent object data without needing to load it
            // again from the database
            nextItem.ParentId = currentId;
            nextItem.Parent = lastSuccessfullyParsed;

            lastSuccessfullyParsed = nextItem;
        }

        // If no parts were seen, then just return the root folder
        if (!partSeen)
            return new InternalPathParseResult(null);

        if (lastSuccessfullyParsed == null)
            throw new Exception("Logic error in internal path parse");

        return new InternalPathParseResult(lastSuccessfullyParsed);
    }

    [NonAction]
    private async Task<StorageItem> CreateMultipleFolders(StorageItem? startFolder, string pathToCreate, User user,
        FileAccess readAccessToCreateWith, FileAccess writeAccessToCreateWith, User ownerToCreateWith,
        bool finalFolderIsImportant, bool finalFolderIsPropertiesLocked,
        bool restore, bool lastFolderWillReceiveAnItem = true)
    {
        var pathsToCreate = pathToCreate.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (pathsToCreate.Length < 1)
            throw new ArgumentException("No paths to create specified");

        StorageItem? currentFolder = startFolder;

        foreach (var pathPart in pathsToCreate)
        {
            // Don't accidentally add subfolders to files
            if (currentFolder != null && currentFolder.Ftype != FileType.Folder)
            {
                throw new HttpResponseException
                {
                    Status = (int)HttpStatusCode.Conflict,
                    Value = $"While creating target path encountered a non-folder item {currentFolder.Name}",
                };
            }

            // Try to create this element
            if (currentFolder == null)
            {
                if (!user.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
                {
                    throw new HttpResponseException
                    {
                        Status = (int)HttpStatusCode.Conflict,
                        Value =
                            "While creating target path, folder creating failed because only admins can create root " +
                            $"items, path part: {pathPart}",
                    };
                }
            }
            else if (!currentFolder.IsWritableBy(user))
            {
                throw new HttpResponseException
                {
                    Status = (int)HttpStatusCode.Conflict,
                    Value = $"While creating target path part \"{pathPart}\" an unwritable folder was encountered",
                };
            }

            if (string.IsNullOrEmpty(pathPart))
                throw new ArgumentException("Encountered an empty folder name to create");

            // Creating the folder is allowed
            var newFolder = new StorageItem
            {
                Name = pathPart,
                Ftype = FileType.Folder,
                AllowParentless = currentFolder == null,
                ReadAccess = readAccessToCreateWith,
                WriteAccess = writeAccessToCreateWith,
                OwnerId = user.Id,
                Parent = currentFolder,

                // We can actually assume the size to be 1 here as the final folder will have the moved / restored item
                // (or we can reset that at the end) and each intermediate path item will have the next path folder in
                // it
                Size = 1,
            };

            await database.StorageItems.AddAsync(newFolder);

            if (restore)
            {
                await database.ActionLogEntries.AddAsync(new ActionLogEntry
                {
                    Message = $"Created a new storage folder named \"{pathPart}\" to perform a file restore",
                    PerformedById = user.Id,
                });
            }
            else
            {
                await database.ActionLogEntries.AddAsync(new ActionLogEntry
                {
                    Message = $"Created a new storage folder named \"{pathPart}\" to perform an item move",
                    PerformedById = user.Id,
                });
            }

            currentFolder = newFolder;
        }

        if (currentFolder == null)
            throw new Exception("Logic error in creating multiple folders");

        // Only the last folder has the original user restored as the owner
        currentFolder.OwnerId = ownerToCreateWith.Id;
        currentFolder.Important = finalFolderIsImportant;
        currentFolder.ModificationLocked = finalFolderIsPropertiesLocked;
        currentFolder.LastModifiedById = user.Id;

        if (!lastFolderWillReceiveAnItem)
            currentFolder.Size = 0;

        return currentFolder;
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

    private class InternalPathParseResult
    {
        public StorageItem? FullyParsed;

        public StorageItem? ParsedUntil;

        public string? PartiallyParsedPath;

        public bool Parsed;

        public InternalPathParseResult(StorageItem? fullyParsed)
        {
            FullyParsed = fullyParsed;
            Parsed = true;
        }

        public InternalPathParseResult(IEnumerable<string> fullyParsed, StorageItem? lastSuccessfullyParsed)
        {
            PartiallyParsedPath = string.Join('/', fullyParsed);
            ParsedUntil = lastSuccessfullyParsed;
        }
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
