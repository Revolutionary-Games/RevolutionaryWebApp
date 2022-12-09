namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Forms;
using Shared.Models;
using Shared.Models.Enums;

[ApiController]
[Route("api/v1/[controller]")]
public class CISecretsController : Controller
{
    private readonly ILogger<CISecretsController> logger;
    private readonly NotificationsEnabledDb database;

    public CISecretsController(ILogger<CISecretsController> logger, NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    [HttpGet("{projectId:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<List<CISecretDTO>>> Get([Required] long projectId, [Required] string sortColumn,
        [Required] SortDirection sortDirection)
    {
        var project = await database.CiProjects.FindAsync(projectId);

        if (project == null)
            return NotFound();

        IQueryable<CiSecret> query;

        try
        {
            query = database.CiSecrets.Where(s => s.CiProjectId == project.Id)
                .OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        return await query.Select(s => s.GetDTO()).ToListAsync();
    }

    [HttpPost("{projectId:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> Create([Required] long projectId,
        [Required] [FromBody] CreateCISecretForm request)
    {
        var project = await database.CiProjects.FindAsync(projectId);

        if (project == null)
            return NotFound();

        if (await database.CiSecrets.FirstOrDefaultAsync(s => s.CiProjectId == project.Id &&
                s.SecretName == request.SecretName && s.UsedForBuildTypes == request.UsedForBuildTypes) != null)
        {
            return BadRequest("A secret with the given name and type already exists");
        }

        var previousSecretId = await database.CiSecrets.Where(s => s.CiProjectId == project.Id)
            .MaxAsync(s => (long?)s.CiSecretId) ?? 0;

        var user = HttpContext.AuthenticatedUser()!;

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"New secret \"{request.SecretName}\" created for project {project.Id}",
            PerformedById = user.Id,
        });

        await database.CiSecrets.AddAsync(new CiSecret
        {
            CiProjectId = project.Id,
            CiSecretId = previousSecretId + 1,
            SecretName = request.SecretName,
            SecretContent = request.SecretContent ?? string.Empty,
            UsedForBuildTypes = request.UsedForBuildTypes,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("New secret {SecretName} created by {Email} for {Id}", request.SecretName, user.Email,
            project.Id);

        return Ok();
    }

    [HttpDelete("{projectId:long}/{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult> DeleteSecret([Required] long projectId, [Required] long id)
    {
        var project = await database.CiProjects.FindAsync(projectId);

        if (project == null)
            return NotFound();

        var item = await database.CiSecrets.FirstOrDefaultAsync(s =>
            s.CiProjectId == project.Id && s.CiSecretId == id);

        if (item == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Secret \"{item.SecretName}\" ({item.CiSecretId}) deleted from project {project.Id}",
            PerformedById = user.Id,
        });

        database.CiSecrets.Remove(item);

        await database.SaveChangesAsync();

        logger.LogInformation("Secret {SecretName} ({CiSecretId}) deleted by {Email} from project {Id}",
            item.SecretName, item.CiSecretId, user.Email, project.Id);

        return Ok();
    }
}
