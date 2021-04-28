using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Threading.Tasks;
    using Authorization;
    using BlazorPagination;
    using Filters;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/Files")]
    public class StorageFilesController : Controller
    {
        private readonly ILogger<StorageFilesController> logger;
        private readonly NotificationsEnabledDb database;

        public StorageFilesController(ILogger<StorageFilesController> logger, NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
        }

        [HttpGet("ItemFromPath")]
        public async Task<ActionResult<PathParseResult>> ParsePath([MaxLength(500)] string path)
        {
            var user = HttpContext.AuthenticatedUser();

            if (user != null && HttpContext.AuthenticatedUserRestriction() != AuthenticationScopeRestriction.None)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status403Forbidden,
                    Value = "You authentication method has incorrect access restriction for this endpoint"
                };
            }

            // Return root folder if the path is empty
            if (string.IsNullOrEmpty(path))
            {
                return new PathParseResult()
                {
                    FinalItem = null
                };
            }

            var pathParts = path.Split('/');

            StorageItem currentItem = null;
            StorageItem parentItem = null;

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
                    await database.StorageItems.AsQueryable()
                        .FirstOrDefaultAsync(i => i.ParentId == currentId && i.Name == part);

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

            return new PathParseResult()
            {
                ParentFolder = parentItem?.GetDTO(),
                FinalItem = currentItem?.GetDTO()
            };
        }

        [HttpGet("folderContents")]
        public async Task<ActionResult<PagedResult<StorageItemInfo>>> GetFolderContents([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 500)] int pageSize, long? parentId = null)
        {
            // NOTE: we don't verify the parent accesses recursively, so for example if a folder has public read, but
            // it is contained in a private folder, the contents can be read through this if the parent id is known.

            StorageItem item = null;
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
                query = database.StorageItems.AsQueryable()
                    .Where(i => i.ParentId == parentId).ToAsyncEnumerable()
                    .OrderByDescending(p => p.Ftype).ThenBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            // Filter out objects not readable by current user
            // NOTE: that as a special case folder owner always sees all items, even if their contents are not readable
            // (this is to make things consistent with the notifications hub)
            var reader = HttpContext.AuthenticatedUser();

            if (item == null || item.OwnerId == null || item.OwnerId != reader.Id)
            {
                query = query.Where(i => i.IsReadableBy(reader));
            }

            // And then return the contents of this folder to the requester
            var objects = await query.ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetInfo());
        }

        [NonAction]
        private async Task<StorageItem> FindAndCheckAccess(long id, bool read = true)
        {
            var item = await database.StorageItems.FindAsync(id);

            if (item == null)
                return null;

            var user = HttpContext.AuthenticatedUser();

            if (user != null && HttpContext.AuthenticatedUserRestriction() != AuthenticationScopeRestriction.None)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status403Forbidden,
                    Value = "You authentication method has incorrect access restriction for this endpoint"
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
    }
}
