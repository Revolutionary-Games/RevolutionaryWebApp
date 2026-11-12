namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Translations;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[APIKeyScope(APIKeyScope.Translations)]
[ApiController]
[Route("api/v1/[controller]")]
public class TranslationProblemsController : Controller
{
    private readonly NotificationsEnabledDb database;

    public TranslationProblemsController(NotificationsEnabledDb database)
    {
        this.database = database;
    }

    [HttpGet]
    public async Task<ActionResult<TranslationProblemDTO[]>> Get(long projectId, string key, string language)
    {
        if (!await database.TranslationProjectLanguages.AnyAsync(item => item.ProjectId == projectId &&
                item.LanguageCode == language && item.Enabled && item.Project.Enabled))
        {
            return NotFound();
        }

        return await database.TranslationProblems.AsNoTracking().Where(problem => problem.ProjectId == projectId &&
                problem.Key == key && problem.LanguageCode == language)
            .OrderBy(problem => problem.Kind).Select(problem => new TranslationProblemDTO
            {
                Id = problem.Id, Kind = problem.Kind, Message = problem.Message, Ignored = problem.Ignored,
                IgnoredByUserId = problem.IgnoredByUserId, IgnoredAt = problem.IgnoredAt,
            }).ToArrayAsync();
    }

    [HttpPost("{id:long}/ignore")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
    [RequireTranslatorAgreement]
    [CountContribution(5)]
    public async Task<IActionResult> Ignore(long id)
    {
        var projectId = await database.TranslationProblems.Where(problem => problem.Id == id)
            .Select(problem => (long?)problem.ProjectId).FirstOrDefaultAsync();
        if (projectId == null)
            return NotFound();

        await using var projectLock = await TranslationProjectAdvisoryLock.AcquireAsync(database, projectId.Value,
            HttpContext.RequestAborted);
        var problem = await database.TranslationProblems.Include(item => item.Value).ThenInclude(value => value.Unit)
            .ThenInclude(unit => unit.Project).Include(item => item.Value).ThenInclude(value => value.Language)
            .FirstOrDefaultAsync(item => item.Id == id);
        if (problem == null || !problem.Value.Unit.Active || !problem.Value.Language.Enabled ||
            !problem.Value.Unit.Project.Enabled)
        {
            return NotFound();
        }

        var project = problem.Value.Unit.Project;
        var actor = HttpContext.AuthenticatedUserOrThrow();
        if (!TranslationPermissions.CanTranslate(actor.AccessCachedGroupsOrThrow(),
                problem.LanguageCode == project.SourceLanguage))
        {
            return this.WorkingForbid("You don't have permission to translate this");
        }

        if (project.ClosedForMaintenance)
            return Conflict("This project is closed for maintenance");

        if (problem.Ignored)
            return NoContent();

        problem.Ignored = true;
        problem.IgnoredByUserId = actor.Id;
        problem.IgnoredAt = DateTime.UtcNow;
        await database.SaveChangesAsync();
        return NoContent();
    }
}
