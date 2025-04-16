namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Models.Pages;
using Utilities;

[Route("livePreview")]
public class LivePreviewController : Controller
{
    private readonly ApplicationDbContext database;
    private readonly IPageRenderer pageRenderer;

    public LivePreviewController(ApplicationDbContext database, IPageRenderer pageRenderer)
    {
        this.database = database;
        this.pageRenderer = pageRenderer;
    }

    [HttpGet]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true,
        AllowFallbackGroup = GroupType.PostEditor)]
    public async Task<IActionResult> Get([Required] long pageId)
    {
        var timer = new Stopwatch();
        timer.Start();

        var page = await database.VersionedPages.FindAsync(pageId);

        if (page == null || page.Deleted)
        {
            return NotFound("No page to preview exists with the ID");
        }

        if (page.Type != PageType.Post && page.Type != PageType.NormalPage)
        {
            return BadRequest("Type of page is not compatible with this preview");
        }

        var groups = HttpContext.AuthenticatedUserOrThrow().AccessCachedGroupsOrThrow();

        if (page.Visibility == PageVisibility.VisibleToDevelopers)
        {
            if (!groups.HasAccessLevel(GroupType.Developer))
            {
                return this.WorkingForbid("Only developers can view pages with this visibility");
            }
        }

        // TODO: should this check more in detail with the page permissions?

        var parts = await LiveController.GetSiteLayoutParts(database, page.Type);

        // Extra image etc. resources go to the "live" endpoint that are linked in metadata
        var rendered =
            await pageRenderer.RenderPage(page, $"{Request.Scheme}://{Request.Host}/live/", parts, false, timer);

        return View("Pages/_PagePreview", rendered);
    }
}
