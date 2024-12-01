namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using DevCenterCommunication.Models;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Pages;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Models.Pages;
using SharedBase.Utilities;
using Utilities;

/// <summary>
///   Allows managing media folders
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class MediaFolderController : Controller
{
    private readonly ILogger<MediaFolderController> logger;
    private readonly NotificationsEnabledDb database;

    public MediaFolderController(ILogger<MediaFolderController> logger, NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    /// <summary>
    ///   Lists subfolders. Always returns all results as that makes things a lot simpler in terms of permission
    ///   checks.
    /// </summary>
    /// <returns>Full list of subfolders, or an error result</returns>
    [HttpGet("folders")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.User)]
    public async Task<ActionResult<List<MediaFolderInfo>>> ListFolderSubFolders([Required] string sortColumn,
        [Required] SortDirection sortDirection, long? parentFolder = null)
    {
        if ((await CheckFolderAccess(parentFolder)).HasAccess == false)
            return NotFound("Not found or you don't have access to the folder");

        IQueryable<MediaFolder> query;

        try
        {
            query = database.MediaFolders.AsNoTracking().Where(f => f.ParentFolderId == parentFolder)
                .OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var user = HttpContext.AuthenticatedUser();
        var userId = user?.Id;
        var groups = user?.AccessCachedGroupsOrThrow();

        // TODO: see if there is some more optimal way to do this
        var all = query.AsEnumerable();

        // Filter out non-readable items
        all = all.Where(f => f.OwnedById == userId || f.LastModifiedById == userId || (groups != null &&
            (groups.HasGroup(GroupType.Admin) || groups.HasGroup(f.FolderModifyAccess) ||
                groups.HasGroup(f.ContentReadAccess))));

        return all.Select(i => i.GetInfo()).ToList();
    }

    [HttpGet("items")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.User)]
    public async Task<ActionResult<PagedResult<MediaFileInfo>>> ListFolderContents([Required] long? parentFolder,
        [Required] string sortColumn, [Required] SortDirection sortDirection,
        [Required] [Range(1, int.MaxValue)] int page, [Required] [Range(1, 100)] int pageSize)
    {
        if (parentFolder == null)
            return BadRequest("Root folder cannot have media items in it");

        if ((await CheckFolderAccess(parentFolder)).HasAccess == false)
            return NotFound("Not found or you don't have access to the folder");

        IQueryable<MediaFile> query;

        try
        {
            query = database.MediaFiles.AsNoTracking().Where(f => f.FolderId == parentFolder)
                .OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var user = HttpContext.AuthenticatedUser();
        var userId = user?.Id;
        var groups = user?.AccessCachedGroupsOrThrow();

        // Apply filtering, due to it being complex it sadly needs to be performed on the client side instead of the DB
        query = query.AsEnumerable().Where(f =>
            f.UploadedById == userId || f.LastModifiedById == userId || (groups != null &&
                (groups.HasGroup(f.MetadataVisibility) || groups.HasGroup(f.ModifyAccess) ||
                    groups.HasGroup(GroupType.Admin)))).AsQueryable();

        // Use non-async variant as this is client-side calculated already
        var objects = query.ToPagedResult(page, pageSize);

        return objects.ConvertResult(i => i.GetInfo());
    }

    [HttpGet("parsePath")]
    public async Task<ActionResult<MediaPathParseResult>> ParseMediaPath([MaxLength(500)] string? path)
    {
        var user = HttpContext.AuthenticatedUser();

        if (user != null && HttpContext.AuthenticatedUserRestriction() != AuthenticationScopeRestriction.None)
        {
            return this.WorkingForbid("You authentication method has incorrect access restriction for this endpoint");
        }

        // Root folder
        if (string.IsNullOrWhiteSpace(path))
        {
            return new MediaPathParseResult
            {
                ParentFolder = null,
            };
        }

        var pathParts = path.Split('/');

        MediaFolder? currentItem = null;

        // This variant of path parse doesn't return the final item as compared to the main storage path parse
        // MediaFolder? parentItem = null;

        foreach (var part in pathParts)
        {
            // Skip empty parts to support starting with a slash or having multiple in a row
            if (string.IsNullOrEmpty(part))
                continue;

            var currentId = currentItem?.Id;
            var nextItem =
                await database.MediaFolders.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.ParentFolderId == currentId && i.Name == part);

            if (nextItem == null || !nextItem.IsReadableBy(user))
            {
                return NotFound($"Path part \"{part}\" doesn't exist or you don't have permission to view it. " +
                    "Logging in may help.");
            }

            // parentItem = currentItem;
            currentItem = nextItem;
        }

        return new MediaPathParseResult
        {
            ParentFolder = currentItem?.GetDTO(),
        };
    }

    [HttpPost("folders")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.User)]
    public async Task<IActionResult> CreateSubFolder([Required] long? parentFolder,
        [Required] [FromBody] MediaFolderDTO request)
    {
        var (hasAccess, folder) = await CheckFolderAccess(parentFolder);
        if (!hasAccess)
            return NotFound("Not found or you don't have access to parent folder");

        if (request.Name.StartsWith('@'))
            return BadRequest("Name may not start with @");

        var user = HttpContext.AuthenticatedUserOrThrow();
        var userGroups = user.AccessCachedGroupsOrThrow();

        if (request.ContentReadAccess == GroupType.NotLoggedIn || request.ContentWriteAccess == GroupType.NotLoggedIn ||
            request.FolderModifyAccess == GroupType.NotLoggedIn ||
            request.SubFolderModifyAccess == GroupType.NotLoggedIn)
        {
            return BadRequest("Can't give access to not logged in group");
        }

        var newFolder = new MediaFolder(request.Name)
        {
            ParentFolder = folder,
            ContentReadAccess = request.ContentReadAccess,
            ContentWriteAccess = request.ContentWriteAccess,
            FolderModifyAccess = request.FolderModifyAccess,
            OwnedById = user.Id,
        };

        if (folder == null)
        {
            // Creating root level folder
            if (!userGroups.HasGroup(GroupType.Admin))
                return this.WorkingForbid("Only admins can create top level folders");

            await database.ActionLogEntries.AddAsync(
                new ActionLogEntry($"Folder \"{newFolder.Name.Truncate()}\" in root folder created")
                {
                    PerformedById = user.Id,
                });
        }
        else
        {
            if (!userGroups.HasGroup(folder.SubFolderModifyAccess) && !userGroups.HasGroup(GroupType.Admin))
                return this.WorkingForbid("You lack the permission to create subfolders in this folder");

            await database.ActionLogEntries.AddAsync(new ActionLogEntry(
                $"Folder \"{newFolder.Name.Truncate()}\" created in folder " +
                $"\"{folder.Name.Truncate()}\" ({folder.Id})")
            {
                PerformedById = user.Id,
            });
        }

        if (await database.MediaFolders.AnyAsync(f => f.ParentFolderId == parentFolder && f.Name == request.Name))
        {
            return BadRequest("That folder name is already in use");
        }

        await database.MediaFolders.AddAsync(newFolder);

        await database.SaveChangesAsync();
        logger.LogInformation("New folder {Name} ({Id1}) created as a subfolder in {Id2} by {Email}", newFolder.Name,
            newFolder.Id, parentFolder, user.Email);

        return Ok("Folder created");
    }

    [HttpPut("folders/{folderId:long}")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.User)]
    public async Task<IActionResult> UpdateFolderData([Required] long folderId,
        [Required] [FromBody] MediaFolderDTO request)
    {
        var (hasAccess, folder) = await CheckFolderAccess(folderId);
        if (!hasAccess || folder == null)
            return NotFound("Not found or you don't have access to the folder");

        var user = HttpContext.AuthenticatedUserOrThrow();
        var userGroups = user.AccessCachedGroupsOrThrow();

        if (!userGroups.HasGroup(folder.FolderModifyAccess) && !userGroups.HasGroup(GroupType.Admin))
            return this.WorkingForbid("You lack permission to edit this folder");

        if (request.Name != folder.Name)
        {
            if (await database.MediaFolders.AnyAsync(f =>
                    f.ParentFolderId == folder.ParentFolderId && f.Name == request.Name))
            {
                return BadRequest("New folder name is already in use");
            }

            if (request.Name.StartsWith('@'))
                return BadRequest("Name may not start with @");
        }

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(folder, request);

        if (!changes)
            return Ok("No changes done");

        await database.ActionLogEntries.AddAsync(
            new ActionLogEntry($"Folder \"{folder.Name.Truncate()}\" ({folder.Id}) edited", description)
            {
                PerformedById = user.Id,
            });
        folder.BumpUpdatedAt();

        // Should reset the delay-delete flag
        await ResetDelayDeleteStatus(folder, user);

        await database.SaveChangesAsync();

        logger.LogInformation("Folder {Name} ({Id}) edited by {Email}, changes: {Description}", folder.Name, folder.Id,
            user.Email, description);

        return Ok();
    }

    [HttpDelete("folders")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.User)]
    public async Task<IActionResult> DeleteFolder([Required] long folderId, bool delayedDelete)
    {
        var (hasAccess, folder) = await CheckFolderAccess(folderId);
        if (!hasAccess || folder == null)
            return NotFound("Not found or you don't have access to the folder");

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (!user.AccessCachedGroupsOrThrow().HasGroup(folder.FolderModifyAccess))
            return this.WorkingForbid("You lack permission to delete this folder");

        if (delayedDelete)
        {
            // TODO: implement this (needs a periodic job to delete empty folders with the flag set)
            return Problem("Delay deleted mode is not implemented yet");
        }

        // Should reset the delay-delete status (when that is not wanted)
        await ResetDelayDeleteStatus(folder, user);

        // Cannot delete a folder with any items in it
        if (await database.MediaFolders.AnyAsync(f => f.ParentFolderId == folderId))
            return BadRequest("Cannot delete a folder that has any subfolders");

        if (await database.MediaFiles.AnyAsync(f => f.FolderId == folderId))
            return BadRequest("Cannot delete a folder that has any media items in it (including purge-pending ones)");

        database.MediaFolders.Remove(folder);

        await database.ActionLogEntries.AddAsync(
            new ActionLogEntry($"Media Folder {folder.Id} ({folder.Name.Truncate()}) deleted")
            {
                PerformedById = user.Id,
            });

        logger.LogInformation("Media Folder {Id} ({Name}) deleted by {Email}", folder.Id, folder.Name, user.Email);
        return Ok("Folder deleted");
    }

    [NonAction]
    private async Task ResetDelayDeleteStatus(MediaFolder folder, User user)
    {
        if (folder.DeleteIfEmpty)
        {
            folder.DeleteIfEmpty = false;

            await database.ActionLogEntries.AddAsync(
                new ActionLogEntry(
                    $"Media Folder {folder.Id} ({folder.Name.Truncate()}) will no longer be auto-deleted")
                {
                    PerformedById = user.Id,
                });

            logger.LogInformation("Cleared folder auto delete flag for {Name} by {Email}", folder.Name, user.Email);
            await database.SaveChangesAsync();
        }
    }

    private async Task<(bool HasAccess, MediaFolder? Folder)> CheckFolderAccess(long? folderId)
    {
        var user = HttpContext.AuthenticatedUser();

        if (folderId == null)
        {
            // Listing contents of root folder is allowed by all, visibility is checked when returning the items
            return (true, null);
        }

        // No anon access to any folder
        if (user == null)
            return (false, null);

        var groups = user.AccessCachedGroupsOrThrow();

        var folder = await database.MediaFolders.FindAsync(folderId);

        // Non-existent folder
        if (folder == null)
            return (false, null);

        if (user.Id == folder.OwnedById || user.Id == folder.LastModifiedById)
            return (true, folder);

        if (groups.HasGroup(GroupType.Admin))
            return (true, folder);

        if (groups.HasGroup(folder.ContentReadAccess))
            return (true, folder);

        // It's probably good that write access implies read as well
        if (groups.HasGroup(folder.ContentWriteAccess))
            return (true, folder);

        if (groups.HasGroup(folder.FolderModifyAccess))
            return (true, folder);

        return (false, folder);
    }
}
