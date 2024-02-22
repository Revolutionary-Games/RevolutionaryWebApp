namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models.Interfaces;
using Shared;
using Shared.Models.Enums;
using Utilities;

public abstract class BaseSoftDeletedResourceController<TModel, TInfo, TDTO> : Controller
    where TModel : class, ISoftDeletable, IDTOCreator<TDTO>, IInfoCreator<TInfo>
    where TInfo : class
    where TDTO : class
{
    protected abstract ILogger Logger { get; }
    protected abstract DbSet<TModel> Entities { get; }

    protected abstract GroupType RequiredViewAccessLevel { get; }

    [HttpGet]
    public async Task<ActionResult<PagedResult<TInfo>>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 50)] int pageSize, bool deleted = false)
    {
        if (!HttpContext.HasAuthenticatedUserWithGroup(RequiredViewAccessLevel,
                AuthenticationScopeRestriction.None))
        {
            return Forbid();
        }

        // Only admins can view deleted items
        if (deleted &&
            !HttpContext.HasAuthenticatedUserWithGroup(GroupType.Admin, AuthenticationScopeRestriction.None))
        {
            throw new HttpResponseException
                { Status = StatusCodes.Status403Forbidden, Value = "You must be an admin to view this" };
        }

        IQueryable<TModel> query;

        try
        {
            query = GetEntitiesForJustInfo(deleted, sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            Logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetInfo());
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<TDTO>> GetSingle([Required] long id)
    {
        if (!HttpContext.HasAuthenticatedUserWithGroup(RequiredViewAccessLevel,
                AuthenticationScopeRestriction.None))
        {
            return Forbid();
        }

        var item = await FindAndCheckAccess(id);

        if (item == null)
            return NotFound();

        // Only admins can view deleted items
        if (item.Deleted &&
            !HttpContext.HasAuthenticatedUserWithGroup(GroupType.Admin, AuthenticationScopeRestriction.None))
        {
            return NotFound();
        }

        return item.GetDTO();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpDelete("{id:long}")]
    public async Task<ActionResult> DeleteResource([Required] long id)
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

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpPost("{id:long}/restore")]
    public async Task<ActionResult> RestoreResource([Required] long id)
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
    protected async Task<TModel?> FindAndCheckAccess(long id)
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

    /// <summary>
    ///   Entities meant for just fetching the info. Should skip large DB fields and call AsNoTracking
    /// </summary>
    [NonAction]
    protected virtual IQueryable<TModel> GetEntitiesForJustInfo(bool deleted, string sortColumn,
        SortDirection sortDirection)
    {
        return Entities.AsNoTracking().Where(i => i.Deleted == deleted).OrderBy(sortColumn, sortDirection);
    }
}
