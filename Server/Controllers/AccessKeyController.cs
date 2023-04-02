namespace ThriveDevCenter.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class AccessKeyController : Controller
{
    private readonly ILogger<AccessKeyController> logger;
    private readonly NotificationsEnabledDb database;

    public AccessKeyController(ILogger<AccessKeyController> logger,
        NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpGet]
    public async Task<PagedResult<AccessKeyDTO>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 50)] int pageSize)
    {
        IQueryable<AccessKey> query;

        try
        {
            query = database.AccessKeys.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpPost]
    public async Task<IActionResult> CreateNew([Required] [FromBody] AccessKeyDTO newKey)
    {
        if (string.IsNullOrWhiteSpace(newKey.Description) || await database.AccessKeys
                .FirstOrDefaultAsync(a => a.Description == newKey.Description) != null)
        {
            return BadRequest("Description is empty or a key with that description already exists");
        }

        var key = new AccessKey
        {
            Description = newKey.Description,
            KeyCode = Guid.NewGuid().ToString(),
            KeyType = newKey.KeyType,
        };

        var action = new AdminAction
        {
            Message = $"New access key ({key.Description}) created with scope: {key.KeyType}",
            PerformedById = HttpContext.AuthenticatedUser()!.Id,
        };

        await database.AccessKeys.AddAsync(key);
        await database.AdminActions.AddAsync(action);
        await database.SaveChangesAsync();

        return Ok($"Created new key \"{key.Id}\" with code: {key.KeyCode}");
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeleteKey([Required] long id)
    {
        var key = await database.AccessKeys.FindAsync(id);

        if (key == null)
            return NotFound();

        var action = new AdminAction
        {
            Message = $"Access key {key.Id} was deleted",
            PerformedById = HttpContext.AuthenticatedUser()!.Id,
        };

        database.AccessKeys.Remove(key);
        await database.AdminActions.AddAsync(action);
        await database.SaveChangesAsync();

        return Ok();
    }
}
