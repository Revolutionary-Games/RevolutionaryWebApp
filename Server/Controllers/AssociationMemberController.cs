namespace ThriveDevCenter.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Hangfire;
using Jobs;
using Microsoft.AspNetCore.Mvc;
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

        if (request.Email.Trim() != request.Email)
            return BadRequest("Email has trailing or preceding whitespace");

        var user = HttpContext.AuthenticatedUser()!;

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(member, request);

        if (!changes)
            return Ok();

        if (await ConflictsWithCurrentPresident(member.Id, member.CurrentPresident))
            return BadRequest("There can only be one association president at once");

        member.BumpUpdatedAt();

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Association member {member.Id} edited",

            // TODO: there could be an extra info property where the description is stored
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Association member {Id} edited by {Email}, changes: {Description}", member.Id,
            user.Email, description);

        if (member.CurrentPresident)
        {
            // We don't create permanent log here as I'm too lazy to check if the changed fields include the president
            // field
            logger.LogInformation("Association member {Id} ({Email}) is the current president", member.Id,
                member.Email);
        }

        jobClient.Enqueue<CheckAssociationStatusForUserJob>(x => x.Execute(member.Email, CancellationToken.None));
        return Ok();
    }

    [HttpDelete("{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> DeleteMember([Required] long id)
    {
        var member = await database.AssociationMembers.FindAsync(id);

        if (member == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        database.AssociationMembers.Remove(member);

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Association member {member.Id} ({member.Email}) deleted",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        // The DTO is serialized here as date only instances are not supported
        logger.LogInformation("Association member {Id} deleted by {Email}, full data: {Data}", member.Id,
            user.Email, JsonSerializer.Serialize(member.GetDTO()));

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

        if (await ConflictsWithCurrentPresident(-1, request.CurrentPresident))
            return BadRequest("There can only be one association president at once");

        var user = HttpContext.AuthenticatedUser()!;

        member = new AssociationMember(request.FirstNames, request.LastName, request.Email,
            DateOnly.FromDateTime(request.JoinDate),
            request.CountryOfResidence, request.CityOfResidence)
        {
            BoardMember = request.BoardMember,
            CurrentPresident = request.CurrentPresident,
            IsThriveDeveloper = request.IsThriveDeveloper,
            HasBeenBoardMember = request.HasBeenBoardMember,
        };
        await database.AssociationMembers.AddAsync(member);

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = member.CurrentPresident ?
                $"New association president {member.Email} created" :
                $"New association member {member.Email} created",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Association member {Id} ({Email}) created by {Email2}", member.Id, member.Email,
            user.Email);

        if (member.CurrentPresident)
        {
            logger.LogInformation("Association member {Id} ({Email}) is the current president", member.Id,
                member.Email);
        }

        jobClient.Enqueue<CheckAssociationStatusForUserJob>(x => x.Execute(member.Email, CancellationToken.None));
        return Ok();
    }

    [NonAction]
    public async Task<bool> ConflictsWithCurrentPresident(long editedUser, bool isPresident)
    {
        // If the edited member is not the president, it can't conflict
        if (!isPresident)
            return false;

        var currentPresident = await database.AssociationMembers.FirstOrDefaultAsync(a => a.CurrentPresident);

        if (currentPresident == null)
            return false;

        return currentPresident.Id != editedUser;
    }
}
