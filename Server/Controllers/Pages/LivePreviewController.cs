namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services;
using Shared.Models.Enums;

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

        var rendered = await pageRenderer.RenderPage(page, timer);

        return View("Pages/_PagePreview", rendered);
    }
}
