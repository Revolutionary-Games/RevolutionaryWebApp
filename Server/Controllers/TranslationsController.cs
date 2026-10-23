namespace RevolutionaryWebApp.Server.Controllers;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared;
using Shared.Models;

/// <summary>
///   Translation project top level data controller
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class TranslationsController : Controller
{
    private readonly ApplicationDbContext database;

    public TranslationsController(ApplicationDbContext database)
    {
        this.database = database;
    }

    [HttpGet("projects")]
    public async Task<ActionResult<TranslationProjectDTO[]>> Projects(
        [FromQuery] SortDirection sortDirection = SortDirection.Ascending)
    {
        var query = database.TranslationProjects.AsNoTracking().Where(p => p.Enabled);
        query = sortDirection == SortDirection.Descending ? query.OrderByDescending(p => p.Name) :
            query.OrderBy(p => p.Name);
        var projects = await query.ToListAsync();
        var groups = HttpContext.AuthenticatedUser()?.AccessCachedGroupsOrThrow();
        var canEditSource = groups != null && TranslationPermissions.CanTranslate(groups, true);
        return projects.Select(p => p.GetDTO(canEditSource)).ToArray();
    }

    [HttpGet("projects/{slug}")]
    public async Task<ActionResult<TranslationProjectDTO>> Project(string slug)
    {
        var project = await database.TranslationProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug && p.Enabled);
        if (project == null)
            return NotFound();

        var groups = HttpContext.AuthenticatedUser()?.AccessCachedGroupsOrThrow();
        var canEditSource = groups != null && TranslationPermissions.CanTranslate(groups, true);
        return project.GetDTO(canEditSource);
    }

    [HttpGet("projects/{slug}/languages")]
    [OutputCache(PolicyName = "ShortLived")]
    public async Task<ActionResult<TranslationLanguageDTO[]>> Languages(string slug,
        CancellationToken cancellationToken)
    {
        var project = await database.TranslationProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug && p.Enabled, cancellationToken);
        if (project == null)
            return NotFound();

        var languages = await database.TranslationProjectLanguages.AsNoTracking()
            .Where(l => l.ProjectId == project.Id && l.Enabled).OrderBy(l => l.DisplayName)
            .Select(l => new TranslationLanguageDTO
            {
                LanguageCode = l.LanguageCode,
                DisplayName = l.DisplayName,
                Total = database.TranslationUnits.Count(u => u.ProjectId == project.Id && u.Active),
                NeedsTranslation = database.TranslationValues.Count(v => v.ProjectId == project.Id &&
                    v.LanguageCode == l.LanguageCode &&
                    v.State == TranslationState.NeedsTranslation),
                Translated = database.TranslationValues.Count(v => v.ProjectId == project.Id &&
                    v.LanguageCode == l.LanguageCode && v.State == TranslationState.Translated),
                Verified = database.TranslationValues.Count(v => v.ProjectId == project.Id &&
                    v.LanguageCode == l.LanguageCode && v.State == TranslationState.Verified),
            })
            .ToArrayAsync(cancellationToken);
        return languages;
    }
}
