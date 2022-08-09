using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Models;
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

    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    [HttpGet]
    public async Task<PagedResult<PatronDTO>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<Patron> query;

        try
        {
            query = database.Patrons.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException() { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }
}