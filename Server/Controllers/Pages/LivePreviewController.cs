namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services;
using Shared.Models.Enums;
using Shared.Models.Pages;

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
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
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

        var parts = await LiveController.GetSiteLayoutParts(database, page.Type);

        var rendered = await pageRenderer.RenderPage(page, parts, false, timer);

        return View("Pages/_PagePreview", rendered);
    }
}
