namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Authorization;
using Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared.Models.Enums;
using Shared.Models.Pages;
using Shared.Notifications;

[ApiController]
[Route("api/v1/[controller]")]
public class EditNotificationsController : Controller
{
    private readonly ApplicationDbContext database;
    private readonly IHubContext<NotificationsHub, INotifications> notifications;

    public EditNotificationsController(ApplicationDbContext database,
        IHubContext<NotificationsHub, INotifications> notifications)
    {
        this.database = database;
        this.notifications = notifications;
    }

    [NonAction]
    public static Task SendEditNotice(IHubContext<NotificationsHub, INotifications> notifications, User user,
        long page, bool saved)
    {
        return notifications.Clients.Group(NotificationGroups.PageEditNotice).ReceiveNotification(new PageEditNotice
        {
            EditorUserId = user.Id,
            PageId = page,
            Saved = saved,
        });
    }

    /// <summary>
    ///   Send a notice that the current user is editing something. This is a get call to ensure this doesn't hit any
    ///   POST rate limits.
    /// </summary>
    /// <param name="pageId">The ID of the page that is edited</param>
    /// <returns>Success when allowed to send</returns>
    [HttpGet]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
    public async Task<IActionResult> SendNotice([Required] long pageId)
    {
        var page = await database.VersionedPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pageId);

        if (page == null || page.Deleted)
            return BadRequest("Page not found to report edit on");

        var user = HttpContext.AuthenticatedUserOrThrow();

        // Check user is allowed to edit page type before sending notification

        await SendEditNotice(notifications, user, page.Id, false);

        return Ok();
    }
}
