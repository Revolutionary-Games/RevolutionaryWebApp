namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Models;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
public class TranslationAgreementController : Controller
{
    private readonly NotificationsEnabledDb database;

    public TranslationAgreementController(NotificationsEnabledDb database)
    {
        this.database = database;
    }

    [HttpPost]
    public async Task<ActionResult<UserDTO>> Agree()
    {
        var user = await database.Users.FindAsync(HttpContext.AuthenticatedUserOrThrow().Id);
        if (user == null)
            return Unauthorized();

        if (!user.AcceptedTranslator)
        {
            await user.ComputeUserGroups(database);

            if (!user.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.User))
            {
                return this.WorkingForbid("You do not have permission to agree to the translation agreement");
            }

            user.AcceptedTranslator = true;
            user.UpdatedAt = DateTime.UtcNow;
            await database.SaveChangesAsync();
        }

        return user.GetDTO(RecordAccessLevel.Private);
    }
}
