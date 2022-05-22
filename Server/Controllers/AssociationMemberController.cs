using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Hangfire;
using Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Models;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class AssociationMemberController : Controller
{
    private readonly ILogger<AssociationMemberController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public AssociationMemberController(ILogger<AssociationMemberController> logger,
        NotificationsEnabledDb database, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    [HttpGet]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<PagedResult<AssociationMemberInfo>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<AssociationMember> query;

        try
        {
            query = database.AssociationMembers.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetInfo());
    }

    [HttpGet("total")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<long> GetMemberCount()
    {
        return await database.AssociationMembers.CountAsync();
    }

    [HttpGet("{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<AssociationMemberDTO>> GetMember([Required] long id)
    {
        var member = await database.AssociationMembers.Where(u => u.Id == id).FirstOrDefaultAsync();

        if (member == null)
            return NotFound();

        logger.LogInformation("Association member {Id} ({Email}) viewed by {Email2}", member.Id, member.Email,
            HttpContext.AuthenticatedUser()!.Email);

        return member.GetDTO();
    }

    [HttpPut("{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> UpdateMember([Required] [FromBody] AssociationMemberDTO request)
    {
        var member = await database.AssociationMembers.FindAsync(request.Id);

        if (member == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(member, request);

        if (!changes)
            return Ok();

        member.BumpUpdatedAt();

        await database.AdminActions.AddAsync(new AdminAction()
        {
            Message = $"Association member {member.Id} edited",

            // TODO: there could be an extra info property where the description is stored
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Association member {Id} edited by {Email}, changes: {Description}", member.Id,
            user.Email, description);

        jobClient.Enqueue<CheckAssociationStatusForUserJob>(x => x.Execute(member.Email, CancellationToken.None));
        return Ok();
    }

    [HttpPost]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> CreateMember([Required] [FromBody] AssociationMemberDTO request)
    {
        var member = await database.AssociationMembers.Where(a => a.Email == request.Email).FirstOrDefaultAsync();

        if (member != null)
            return BadRequest("Email already in use");

        var user = HttpContext.AuthenticatedUser()!;

        member = new AssociationMember(request.FirstNames, request.LastName, request.Email, request.JoinDate,
            request.CountryOfResidence, request.CityOfResidence)
        {
            BoardMember = request.BoardMember,
            IsThriveDeveloper = request.IsThriveDeveloper,
            HasBeenBoardMember = request.HasBeenBoardMember,
        };

        await database.AdminActions.AddAsync(new AdminAction()
        {
            Message = $"New association member {member.Email} created",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Association member {Id} ({Email}) created by {Email2}", member.Id, member.Email,
            user.Email);

        jobClient.Enqueue<CheckAssociationStatusForUserJob>(x => x.Execute(member.Email, CancellationToken.None));
        return Ok();
    }
}
