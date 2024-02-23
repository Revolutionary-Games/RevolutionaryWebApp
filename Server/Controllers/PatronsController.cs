namespace RevolutionaryWebApp.Server.Controllers;

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
public class PatronsController : Controller
{
    private readonly ILogger<PatronsController> logger;
    private readonly ApplicationDbContext database;

    public PatronsController(ILogger<PatronsController> logger,
        ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpGet]
    public async Task<PagedResult<PatronDTO>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 50)] int pageSize, bool includeAccountStatus = false)
    {
        IQueryable<Patron> query;

        try
        {
            query = database.Patrons.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        var converted = objects.ConvertResult(i => i.GetDTO());

        if (includeAccountStatus)
        {
            // TODO: Patreon alias handling if that is added
            var emailsToCheck = converted.Results.Select(p => p.Email).ToHashSet();

            var matched = await database.Users.Where(u => emailsToCheck.Contains(u.Email)).Select(u => u.Email)
                .ToListAsync();

            foreach (var patronDTO in converted.Results)
            {
                patronDTO.HasAccountOnDevCenter = matched.Contains(patronDTO.Email);
            }
        }

        return converted;
    }
}
