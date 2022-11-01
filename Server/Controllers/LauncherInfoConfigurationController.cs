using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Models;
using Utilities;

/// <summary>
///   Allows modification of the launcher info returned by <see cref="LauncherInfoController"/>
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class LauncherInfoConfigurationController : Controller
{
    private readonly ILogger<LauncherInfoConfigurationController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IConfiguration configuration;

    public LauncherInfoConfigurationController(ILogger<LauncherInfoConfigurationController> logger,
        NotificationsEnabledDb database, IConfiguration configuration)
    {
        this.logger = logger;
        this.database = database;
        this.configuration = configuration;
    }

    [HttpGet]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> Get()
    {
        var currentData = await LauncherInfoController.GenerateLauncherInfoObject(database, configuration);

        var validationResult = new List<ValidationResult>();

        if (!Validator.TryValidateObject(currentData, new ValidationContext(currentData), validationResult, true))
        {
            throw new HttpResponseException
            {
                Status = (int)HttpStatusCode.InternalServerError,
                Value = $"Validation failed: {string.Join(", ", validationResult.Select(r => r.ToString()))}",
            };
        }

        string? validationError = null;

        // Extra validations that would be hard to make as validation attributes
        if (currentData.LatestVersionOrNull() == null)
        {
            validationError = "No latest version set";
        }

        if (validationError != null)
        {
            throw new HttpResponseException
            {
                Status = (int)HttpStatusCode.InternalServerError,
                Value = $"Extra validation error: {validationError}",
            };
        }

        // Everything is configured right
        return Ok();
    }

    [HttpGet("mirrors")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<PagedResult<LauncherDownloadMirrorDTO>> GetMirrors([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<LauncherDownloadMirror> query;

        try
        {
            query = database.LauncherDownloadMirrors.AsNoTracking().OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }
}
