namespace ThriveDevCenter.Server.Controllers;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using DevCenterCommunication.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using RecursiveDataAnnotationsValidation;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class PrecompiledObjectController : BaseSoftDeletedResourceController<PrecompiledObject, PrecompiledObjectInfo,
    PrecompiledObjectDTO>
{
    private readonly NotificationsEnabledDb database;

    public PrecompiledObjectController(ILogger<PrecompiledObjectController> logger, NotificationsEnabledDb database)
    {
        Logger = logger;
        this.database = database;
    }

    protected override ILogger Logger { get; }
    protected override DbSet<PrecompiledObject> Entities => database.PrecompiledObjects;
    protected override GroupType RequiredViewAccessLevel => GroupType.NotLoggedIn;

    [HttpPut("{id:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> Update([Required] long id, [Required] [FromBody] PrecompiledObjectDTO request)
    {
        var validator = new RecursiveDataAnnotationValidator();

        var results = new List<ValidationResult>();
        if (!validator.TryValidateObjectRecursive(request, new ValidationContext(request), results))
        {
            return BadRequest("Precompiled object data failed validation");
        }

        if (request.Id != id)
            return BadRequest("ID in model doesn't match URL");

        var precompiledObject = await Entities.FindAsync(request.Id);

        if (precompiledObject == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(precompiledObject, request);

        if (!changes)
            return Ok();

        if (await Entities.AnyAsync(p => p.Name == request.Name && p.Id != request.Id))
            return BadRequest("Name is already in use");

        precompiledObject.BumpUpdatedAt();

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"PrecompiledObject {precompiledObject.Id} edited",

            // TODO: there could be an extra info property where the description is stored
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        Logger.LogInformation("PrecompiledObject {Id} edited by {Email}, changes: {Description}", precompiledObject.Id,
            user.Email, description);
        return Ok();
    }

    [HttpGet("byName/{name}")]
    public async Task<ActionResult<PrecompiledObjectDTO>> GetSingleByName([Required] string name)
    {
        if (name.Length > 200)
            return BadRequest("Too long name");

        if (!HttpContext.HasAuthenticatedUserWithGroup(RequiredViewAccessLevel,
                AuthenticationScopeRestriction.None))
        {
            return Forbid();
        }

        var item = await Entities.FirstOrDefaultAsync(p => p.Name == name);

        if (item == null)
            return NotFound();

        if (!CheckExtraAccess(item))
            return NotFound();

        // Only admins can view deleted items
        if (item.Deleted &&
            !HttpContext.HasAuthenticatedUserWithGroup(GroupType.Admin, AuthenticationScopeRestriction.None))
        {
            return NotFound();
        }

        return item.GetDTO();
    }

    [HttpPost]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.Admin)]
    public async Task<ActionResult> Create([Required] [FromBody] PrecompiledObjectDTO request)
    {
        var validator = new RecursiveDataAnnotationValidator();

        var results = new List<ValidationResult>();
        if (!validator.TryValidateObjectRecursive(request, new ValidationContext(request), results))
        {
            return BadRequest("Precompiled object data failed validation");
        }

        if (request.Deleted)
            return BadRequest("Cannot create object in deleted state");

        if (await Entities.AnyAsync(p => p.Name == request.Name))
            return BadRequest("Name is already in use");

        var user = HttpContext.AuthenticatedUserOrThrow();

        var precompiledObject = new PrecompiledObject(request.Name)
        {
            Public = request.Public,
            TotalStorageSize = 0,
        };

        await Entities.AddAsync(precompiledObject);

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"New PrecompiledObject created with name \"{precompiledObject.Name}\"",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        Logger.LogInformation("New precompiled object {Id} ({Name}) created by {Email}", precompiledObject.Id,
            precompiledObject.Name, user.Email);

        return Ok();
    }

    [NonAction]
    protected override IQueryable<PrecompiledObject> GetEntitiesForJustInfo(bool deleted, string sortColumn,
        SortDirection sortDirection)
    {
        var query = Entities.AsNoTracking().Where(i => i.Deleted == deleted);

        if (HttpContext.HasAuthenticatedUserWithGroup(GroupType.Developer, AuthenticationScopeRestriction.None))
        {
            return query.OrderBy(sortColumn, sortDirection);
        }

        // Hide private items if not a developer
        return query.Where(p => p.Public).OrderBy(sortColumn, sortDirection);
    }

    [NonAction]
    protected override bool CheckExtraAccess(PrecompiledObject resource)
    {
        if (!resource.Public)
        {
            return HttpContext.HasAuthenticatedUserWithGroup(GroupType.Developer, AuthenticationScopeRestriction.None);
        }

        return true;
    }

    [NonAction]
    protected override Task SaveResourceChanges(PrecompiledObject resource)
    {
        resource.BumpUpdatedAt();
        return database.SaveChangesAsync();
    }
}
