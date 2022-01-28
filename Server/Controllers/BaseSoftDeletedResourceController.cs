using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Threading.Tasks;
    using Authorization;
    using BlazorPagination;
    using Filters;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models.Interfaces;
    using Shared;
    using Shared.Models;
    using Utilities;

    public abstract class BaseSoftDeletedResourceController<TModel, TInfo, TDTO> : Controller
        where TModel : class, ISoftDeletable, IDTOCreator<TDTO>, IInfoCreator<TInfo>
        where TInfo : class
        where TDTO : class
    {
        protected abstract ILogger Logger { get; }
        protected abstract DbSet<TModel> Entities { get; }

        [HttpGet]
        public async Task<PagedResult<TInfo>> Get([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 50)] int pageSize, bool deleted = false)
        {
            // Only admins can view deleted items
            if (deleted &&
                !HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Admin, AuthenticationScopeRestriction.None))
            {
                throw new HttpResponseException()
                    { Status = StatusCodes.Status403Forbidden, Value = "You must be an admin to view this" };
            }

            IQueryable<TModel> query;

            try
            {
                query = Entities.Where(p => p.Deleted == deleted).OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                Logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetInfo());
        }

        [HttpGet("{id:long}")]
        public async Task<ActionResult<TDTO>> GetSingle([Required] long id)
        {
            var item = await FindAndCheckAccess(id);

            if (item == null)
                return NotFound();

            // Only admins can view deleted items
            if (item.Deleted &&
                !HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Admin, AuthenticationScopeRestriction.None))
            {
                return NotFound();
            }

            return item.GetDTO();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpDelete("{id:long}")]
        public async Task<ActionResult> DeleteProject([Required] long id)
        {
            var item = await FindAndCheckAccess(id);

            if (item == null)
                return NotFound();

            if (item.Deleted)
                return BadRequest("Resource is already deleted");

            item.Deleted = true;
            await SaveResourceChanges(item);

            return Ok();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpPost("{id:long}/restore")]
        public async Task<ActionResult> RestoreProject([Required] long id)
        {
            var item = await FindAndCheckAccess(id);

            if (item == null)
                return NotFound();

            if (!item.Deleted)
                return BadRequest("Resource is not deleted");

            item.Deleted = false;
            await SaveResourceChanges(item);

            return Ok();
        }

        [NonAction]
        protected async Task<TModel> FindAndCheckAccess(long id)
        {
            var resource = await Entities.FindAsync(id);

            if (resource == null)
                return null;

            if (!CheckExtraAccess(resource))
                return null;

            return resource;
        }

        protected virtual bool CheckExtraAccess(TModel resource)
        {
            return true;
        }

        protected abstract Task SaveResourceChanges(TModel resource);
    }
}
