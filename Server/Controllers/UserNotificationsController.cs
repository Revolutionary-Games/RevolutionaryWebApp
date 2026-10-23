namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared.Models;
using Shared.Models.Enums;

[ApiController]
[Route("api/v1/userNotifications")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
public class UserNotificationsController : Controller
{
    private readonly ApplicationDbContext database;

    public UserNotificationsController(ApplicationDbContext database)
    {
        this.database = database;
    }

    [HttpGet("unreadCount")]
    public async Task<ActionResult<UnreadNotificationCountDTO>> CountUnread()
    {
        var userId = HttpContext.AuthenticatedUserOrThrow().Id;
        var count = await database.UserNotifications.CountAsync(n => n.UserId == userId && n.ReadAt == null);
        return new UnreadNotificationCountDTO { Count = count };
    }

    [HttpGet]
    public async Task<ActionResult<UserNotificationDTO[]>> List([FromQuery] int offset = 0)
    {
        if (offset < 0)
            return BadRequest("Offset must be non-negative");

        var userId = HttpContext.AuthenticatedUserOrThrow().Id;
        var rows = await database.UserNotifications.AsNoTracking().Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(offset).Take(50).ToListAsync();
        return rows.Select(notification => notification.GetDTO()).ToArray();
    }

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id)
    {
        var userId = HttpContext.AuthenticatedUserOrThrow().Id;
        var notification = await database.UserNotifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (notification == null)
            return NotFound();

        notification.ReadAt ??= DateTime.UtcNow;
        await database.SaveChangesAsync();
        return NoContent();
    }
}
