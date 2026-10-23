namespace RevolutionaryWebApp.Server.Controllers;

using System.Linq;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared.Models;
using Shared.Models.Enums;

[ApiController]
[Route("api/v1/[controller]")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
public class TranslationCreditSettingsController : Controller
{
    private readonly ApplicationDbContext database;

    public TranslationCreditSettingsController(ApplicationDbContext database)
    {
        this.database = database;
    }

    [HttpGet]
    public async Task<ActionResult<TranslationCreditSettingsDTO>> Get()
    {
        var userId = HttpContext.AuthenticatedUserOrThrow().Id;
        var value = await database.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => (bool?)u.UseEmailInTranslationCredits).FirstOrDefaultAsync();

        if (value == null)
            return Unauthorized();

        return new TranslationCreditSettingsDTO { UseEmailInTranslationCredits = value.Value };
    }

    [HttpPut]
    public async Task<ActionResult<TranslationCreditSettingsDTO>> Update(
        [FromBody] TranslationCreditSettingsDTO request)
    {
        var user = await database.Users.FindAsync(HttpContext.AuthenticatedUserOrThrow().Id);
        if (user == null)
            return Unauthorized();

        user.UseEmailInTranslationCredits = request.UseEmailInTranslationCredits;
        await database.SaveChangesAsync();
        return new TranslationCreditSettingsDTO
        {
            UseEmailInTranslationCredits = user.UseEmailInTranslationCredits,
        };
    }
}
