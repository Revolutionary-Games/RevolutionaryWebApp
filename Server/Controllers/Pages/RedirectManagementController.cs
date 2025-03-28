namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Pages;
using Shared;
using Shared.Models.Enums;
using Shared.Models.Pages;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
[AuthorizeGroupMemberFilter(RequiredGroup = GroupType.RedirectEditor, AllowAdmin = true)]
public class RedirectManagementController : Controller
{
    private readonly ILogger<RedirectManagementController> logger;
    private readonly NotificationsEnabledDb database;

    public RedirectManagementController(ILogger<RedirectManagementController> logger, NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    [HttpGet]
    public async Task<PagedResult<PageRedirectDTO>> GetRedirectManagementParts([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<PageRedirect> query;

        try
        {
            query = database.PageRedirects.AsNoTracking().OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpPost]
    public async Task<IActionResult> CreatePageRedirect([Required] [FromBody] PageRedirectDTO request)
    {
        if (string.IsNullOrWhiteSpace(request.FromPath) || request.FromPath.StartsWith("http") ||
            request.FromPath.Contains(".."))
        {
            return BadRequest("Invalid source path");
        }

        if (request.FromPath.EndsWith('/'))
            return BadRequest("Source path must not end with a slash");

        if (request.ToUrl.StartsWith("http"))
        {
            // Must be a valid url
            if (!Uri.TryCreate(request.ToUrl, UriKind.Absolute, out _))
                return BadRequest("Invalid target URL");
        }
        else
        {
            // Must be a valid path
            if (request.ToUrl.Contains("..") || request.ToUrl.StartsWith("page:"))
                return BadRequest("Invalid target path");

            if (request.ToUrl.StartsWith("/"))
                return BadRequest("Target path must not start with a slash");
        }

        var redirect = new PageRedirect(request.FromPath, request.ToUrl);

        if (await database.PageRedirects.AnyAsync(r => r.FromPath == redirect.FromPath))
        {
            return BadRequest("Redirect from the given path already exists");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();

        await database.PageRedirects.AddAsync(redirect);
        await database.AdminActions.AddAsync(
            new AdminAction($"New redirect created for: {redirect.FromPath}", "Linking to " + redirect.ToUrl)
            {
                PerformedById = user.Id,
            });

        await database.SaveChangesAsync();

        logger.LogInformation("New redirect {From} -> {To} created by {Email}", redirect.FromPath, redirect.ToUrl,
            user.Email);

        return Ok();
    }

    [HttpGet("{from}")]
    public async Task<ActionResult<PageRedirectDTO>> GetPageRedirect([Required] string from)
    {
        var result = await database.PageRedirects.FindAsync(from);

        if (result == null)
            return NotFound();

        return result.GetDTO();
    }

    [HttpPut]
    public async Task<IActionResult> UpdatePageRedirect([Required] [FromBody] PageRedirectDTO request)
    {
        var redirect = await database.PageRedirects.FindAsync(request.FromPath);

        if (redirect == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(redirect, request);

        if (!changes)
            return Ok();

        redirect.UpdatedAt = DateTime.UtcNow;

        await database.AdminActions.AddAsync(new AdminAction($"Redirect {redirect.FromPath} edited", description)
        {
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Redirect {From} edited by {Email}, changes: {Description}", redirect.FromPath,
            user.Email, description);
        return Ok();
    }

    [HttpDelete("{from}")]
    public async Task<IActionResult> DeletePageRedirect([Required] string from)
    {
        var redirect = await database.PageRedirects.FindAsync(from);

        if (redirect == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        database.PageRedirects.Remove(redirect);

        await database.AdminActions.AddAsync(
            new AdminAction($"Page redirect {redirect.FromPath} deleted", JsonSerializer.Serialize(redirect))
            {
                PerformedById = user.Id,
            });

        await database.SaveChangesAsync();

        logger.LogInformation("Redirect {From} deleted by {Email}", redirect.FromPath, user.Email);
        return Ok();
    }
}
