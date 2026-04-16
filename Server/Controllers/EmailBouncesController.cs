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
[AuthorizeGroupMemberFilter(RequiredGroup = GroupType.Admin)]
public class EmailBouncesController : Controller
{
    private readonly ILogger<EmailBouncesController> logger;
    private readonly NotificationsEnabledDb database;

    public EmailBouncesController(ILogger<EmailBouncesController> logger, NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    [HttpGet]
    public async Task<PagedResult<EmailBounceDTO>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize, string? search)
    {
        IQueryable<EmailBounce> query = database.EmailBounces.AsNoTracking();

        try
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(b => b.Email.Contains(term) || b.NormalizedEmail.Contains(term));
            }

            query = query.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var result = await query.ToPagedResultAsync(page, pageSize);
        return result.ConvertResult(b => b.GetDTO());
    }
}
