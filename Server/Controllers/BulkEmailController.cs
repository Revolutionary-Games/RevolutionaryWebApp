using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
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
using Shared.Forms;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class BulkEmailController : Controller
{
    private readonly ILogger<BulkEmailController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public BulkEmailController(ILogger<BulkEmailController> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    [HttpGet]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<PagedResult<SentBulkEmailDTO>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<SentBulkEmail> query;

        try
        {
            query = database.SentBulkEmails.AsNoTracking().OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException() { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);
        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpPost]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> SendBulkEmail([Required] [FromBody] BulkEmailSendRequestForm request)
    {
        // Rate limit to just a few per day
        var cutoff = DateTime.UtcNow - AppInfo.BulkEmailRateLimitInterval;

        var count = await database.SentBulkEmails.CountAsync(b => b.CreatedAt >= cutoff);

        if (count >= AppInfo.MaxBulkEmailsPerInterval)
        {
            return StatusCode((int)HttpStatusCode.TooManyRequests, "Too many bulk emails have been sent recently");
        }

        var user = HttpContext.AuthenticatedUser()!;
        string? replyTo = null;

        switch (request.ReplyMode)
        {
            case BulkEmailReplyToMode.SendingUser:
                replyTo = $"{user.NameOrEmail} <{user.Email}>";
                break;
            case BulkEmailReplyToMode.DevCenterSendingAddress:
                // We don't set a reply to so just the plain sender address is who gets the replies
                break;
            default:
                return BadRequest("Invalid reply mode");
        }

        var recipientsList = await ComputeRecipientsList(request);

        // Fail if any address doesn't contain a "@" in it
        foreach (var recipient in recipientsList)
        {
            if (!recipient.Contains("@"))
            {
                return BadRequest($"A recipient doesn't appear to be valid email address: {recipient}");
            }
        }

        if (recipientsList.Count < 1)
        {
            return BadRequest("No recipients were left after taking ignores into account");
        }

        var bulkModel = new SentBulkEmail()
        {
            Title = request.Title,
            Recipients = recipientsList.Count,
            SentById = user.Id,
            HtmlBody = request.HTMLBody,
            PlainBody = request.PlainBody,
        };

        // Make sure count is still good
        if (count != await database.SentBulkEmails.CountAsync(b => b.CreatedAt >= cutoff))
        {
            return Problem("The number of sent bulk emails changed while processing, please try again");
        }

        await database.AdminActions.AddAsync(new AdminAction()
        {
            Message = $"A bulk email was sent to {bulkModel.Recipients} people",
            PerformedById = user.Id,
        });

        await database.SentBulkEmails.AddAsync(bulkModel);

        await database.SaveChangesAsync();

        logger.LogInformation("Bulk email ({Id}) send started by {Email}", bulkModel.Id, user.Email);

        // Hopefully starting the jobs here doesn't take too long as this request processing needs to finish in
        // reasonable time
        StartEmailSends(bulkModel.Id, recipientsList, replyTo);

        return Ok();
    }

    [NonAction]
    private async Task<List<string>> ComputeRecipientsList(BulkEmailSendRequestForm request)
    {
        IEnumerable<string> recipients;

        Lazy<Task<List<string>>> devCenterUsers =
            new(() => database.Users.Select(u => u.Email).ToListAsync());

        Lazy<Task<List<string>>> devCenterDevelopers =
            new(() => database.Users.Where(u => u.Developer == true).Select(u => u.Email)
                .ToListAsync());

        Lazy<Task<List<string>>> associationMembers =
            new(() => database.AssociationMembers.Select(a => a.Email)
                .ToListAsync());

        switch (request.RecipientsMode)
        {
            case BulkEmailRecipientsMode.ManualList:
                if (request.ManualRecipients == null)
                    throw new Exception("Manual recipients list is missing");

                recipients = request.ManualRecipients.Split('\n').Select(r => r.Trim().TrimEnd(','))
                    .Where(r => !string.IsNullOrWhiteSpace(r));
                break;
            case BulkEmailRecipientsMode.DevCenterUsers:
                recipients = await devCenterUsers.Value;
                break;
            case BulkEmailRecipientsMode.DevCenterDevelopers:
                recipients = await devCenterDevelopers.Value;
                break;
            case BulkEmailRecipientsMode.AssociationMembers:
                recipients = await associationMembers.Value;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        switch (request.IgnoreMode)
        {
            case BulkEmailIgnoreMode.Nobody:
                break;
            case BulkEmailIgnoreMode.DevCenterUsers:
            {
                var ignoreData = new HashSet<string>(await devCenterUsers.Value);
                recipients = recipients.Where(r => !ignoreData.Contains(r));
                break;
            }

            case BulkEmailIgnoreMode.DevCenterDevelopers:
            {
                var ignoreData = new HashSet<string>(await devCenterDevelopers.Value);
                recipients = recipients.Where(r => !ignoreData.Contains(r));
                break;
            }

            case BulkEmailIgnoreMode.AssociationMembers:
            {
                var ignoreData = new HashSet<string>(await associationMembers.Value);
                recipients = recipients.Where(r => !ignoreData.Contains(r));
                break;
            }

            case BulkEmailIgnoreMode.CLASigned:
            {
                var activeCLA = await database.Clas.FirstOrDefaultAsync(c => c.Active);

                if (activeCLA == null)
                {
                    logger.LogWarning("No active CLA, not able to ignore emails with CLA signature");
                    break;
                }

                var ignoreData = new HashSet<string>(await database.ClaSignatures
                    .Where(s => s.ClaId == activeCLA.Id && s.ValidUntil == null).Select(s => s.Email)
                    .ToListAsync());
                recipients = recipients.Where(r => !ignoreData.Contains(r));
                break;
            }

            default:
                throw new ArgumentOutOfRangeException();
        }

        return recipients.Distinct().ToList();
    }

    [NonAction]
    private void StartEmailSends(long bulkId, IEnumerable<string> recipients, string? replyTo)
    {
        var random = new Random();

        foreach (var chunk in recipients.Chunk(AppInfo.BulkEmailChunkSize))
        {
            // TODO: could make this time dependent on the total number of emails as now small bulk sends can still
            // take an hour for even the first emails to go out
            var delay = TimeSpan.FromSeconds(random.Next(1, AppInfo.MaxBulkEmailDelaySeconds) + 1);

            jobClient.Schedule<SendBulkEmailChunkJob>(
                x => x.Execute(bulkId, chunk.ToList(), replyTo, CancellationToken.None), delay);
        }
    }
}