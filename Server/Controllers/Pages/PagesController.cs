namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Hangfire;
using Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Models;
using Models.Pages;
using Shared;
using Shared.Models.Enums;
using Shared.Models.Pages;

/// <summary>
///   Main controller for <see cref="VersionedPage"/> handling when used as static pages (provides the API for
///   managing the pages)
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class PagesController : BasePageController
{
    private readonly ILogger<PagesController> logger;

    public PagesController(ILogger<PagesController> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient, IHubContext<NotificationsHub, INotifications> notifications) : base(database,
        jobClient, notifications)
    {
        this.logger = logger;
    }

    protected override ILogger Logger => logger;
    protected override PageType HandledPageType => PageType.NormalPage;

    protected override GroupType PrimaryPublisherGroupType => GroupType.SitePagePublisher;
    protected override GroupType ExtraAccessGroup => GroupType.Admin;

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    public override Task<PagedResult<VersionedPageInfo>> GetList([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize, bool deleted = false)
    {
        return base.GetList(sortColumn, sortDirection, page, pageSize, deleted);
    }

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    public override Task<ActionResult<VersionedPageDTO>> GetSingle([Required] long id)
    {
        return base.GetSingle(id);
    }

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    public override Task<ActionResult> CreatePage([Required] [FromBody] VersionedPageDTO pageDTO)
    {
        return base.CreatePage(pageDTO);
    }

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    public override Task<ActionResult> UpdatePage([Required] long id, [Required] [FromBody] VersionedPageDTO pageDTO)
    {
        return base.UpdatePage(id, pageDTO);
    }

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    public override Task<ActionResult> DeleteResource([Required] long id)
    {
        return base.DeleteResource(id);
    }

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    public override Task<ActionResult> RestoreResource([Required] long id)
    {
        return base.RestoreResource(id);
    }

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    public override Task<ActionResult<PagedResult<PageVersionInfo>>> ListResourceVersions([Required] long id,
        [Required] string sortColumn, [Required] SortDirection sortDirection,
        [Required] [Range(1, int.MaxValue)] int page, [Required] [Range(1, 100)] int pageSize)
    {
        return base.ListResourceVersions(id, sortColumn, sortDirection, page, pageSize);
    }

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    public override Task<ActionResult<PageVersionDTO>> GetResourceHistoricalVersion([Required] long id,
        [Required] int version)
    {
        return base.GetResourceHistoricalVersion(id, version);
    }

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    public override Task<ActionResult> RevertResourceVersion([Required] long id,
        [Required] int version)
    {
        return base.RevertResourceVersion(id, version);
    }
}
