using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Collections.Generic;
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
    using Shared.Models.Enums;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class MeetingsController : Controller
    {
        private readonly ILogger<MeetingsController> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;

        public MeetingsController(ILogger<MeetingsController> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
        }

        [HttpGet]
        public async Task<PagedResult<MeetingInfo>> Get([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 100)] int pageSize)
        {
            IQueryable<Meeting> query;

            var access = GetCurrentUserAccess();

            try
            {
                query = database.Meetings.Where(m => m.ReadAccess <= access).OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);
            return objects.ConvertResult(i => i.GetInfo());
        }

        [HttpGet("{id:long}")]
        public async Task<ActionResult<MeetingDTO>> GetMeeting([Required] long id)
        {
            var access = GetCurrentUserAccess();

            var meeting = await database.Meetings.Where(m => m.Id == id && m.ReadAccess <= access)
                .FirstOrDefaultAsync();

            if (meeting == null)
                return NotFound();

            return meeting.GetDTO();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
        [HttpPost("{id:long}/join")]
        public async Task<IActionResult> JoinMeeting([Required] long id)
        {
            var access = GetCurrentUserAccess();
            var meeting = await GetMeetingWithReadAccess(id, access);

            if (meeting == null)
                return NotFound();

            // TODO: should admins be able to always join?
            if (meeting.JoinAccess > access)
                return this.WorkingForbid("You don't have permission to join this meeting");

            if (meeting.EndedAt != null)
                return BadRequest("This meeting has already ended");

            var user = HttpContext.AuthenticatedUser()!;

            // Fail if already joined
            if (await GetMeetingMember(meeting.Id, user.Id) != null)
                return BadRequest("You have already joined this meeting");

            // Don't allow if already started and grace period is over
            if (DateTime.UtcNow > meeting.StartsAt + meeting.JoinGracePeriod)
            {
                return BadRequest("You are too late to join this meeting");
            }

            // Allow join
            await database.ActionLogEntries.AddAsync(new ActionLogEntry()
            {
                Message = $"User joined meeting {meeting.Id}",
                PerformedById = user.Id,
            });

            await database.MeetingMembers.AddAsync(new MeetingMember()
            {
                MeetingId = meeting.Id,
                UserId = user.Id,
            });

            await database.SaveChangesAsync();
            logger.LogInformation("User {Email} joined meeting {Name} ({Id})", user.Email, meeting.Name,
                meeting.Id);

            return Ok();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
        [HttpGet("{id:long}/members")]
        public async Task<ActionResult<PagedResult<MeetingMemberInfo>>> GetMembers([Required] long id,
            [Required] string sortColumn, [Required] SortDirection sortDirection,
            [Required] [Range(1, int.MaxValue)] int page, [Required] [Range(1, 100)] int pageSize)
        {
            var access = GetCurrentUserAccess();
            var meeting = await GetMeetingWithReadAccess(id, access);

            if (meeting == null)
                return NotFound();

            // Only meeting owner and admins can view all members
            var user = HttpContext.AuthenticatedUser()!;

            if (meeting.OwnerId != user.Id && !user.HasAccessLevel(UserAccessLevel.Admin))
                return this.WorkingForbid("You don't have permission to view member list of this meeting");

            IQueryable<MeetingMember> query;

            try
            {
                query = database.MeetingMembers.Where(m => m.MeetingId == meeting.Id)
                    .OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);
            return objects.ConvertResult(i => i.GetInfo());
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
        [HttpGet("{id:long}/members/{userId:long}")]
        public async Task<ActionResult<MeetingMemberDTO>> GetMember([Required] long id, [Required] long userId)
        {
            var access = GetCurrentUserAccess();
            var meeting = await GetMeetingWithReadAccess(id, access);

            if (meeting == null)
                return NotFound();

            // Only meeting owner sees all members, other people can only check their own info
            var user = HttpContext.AuthenticatedUser()!;

            if (meeting.OwnerId != user.Id && userId != user.Id && !user.HasAccessLevel(UserAccessLevel.Admin))
                return this.WorkingForbid("You don't have permission to view other people's join status");

            var member = await GetMeetingMember(meeting.Id, userId);
            if (member == null)
                return NotFound();

            return member.GetDTO();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
        [HttpGet("{id:long}/polls")]
        public async Task<ActionResult<List<MeetingPollDTO>>> GetPolls([Required] long id,
            [Required] string sortColumn, [Required] SortDirection sortDirection)
        {
            var access = GetCurrentUserAccess();
            var meeting = await GetMeetingWithReadAccess(id, access);

            if (meeting == null)
                return NotFound();

            IQueryable<MeetingPoll> query;

            try
            {
                query = database.MeetingPolls.Where(m => m.MeetingId == meeting.Id).OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            return await query.Select(p => p.GetDTO()).ToListAsync();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
        [HttpPost("{id:long}/polls/{pollId:long}/vote")]
        public async Task<ActionResult<List<MeetingPollDTO>>> VoteInPoll([Required] long id,
            [Required] long pollId, [Required] [FromBody] PollVoteData request)
        {
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(request, new ValidationContext(request), errors))
            {
                logger.LogError("Poll vote data didn't pass validation:");

                foreach (var error in errors)
                    logger.LogError("Failure: {Error}", error);

                // TODO: send errors to client?
                return BadRequest("Invalid vote JSON data");
            }

            if (request.SelectedOptions.GroupBy(i => i).Any(g => g.Count() > 1))
            {
                return BadRequest("Can't vote multiple times for the same option");
            }

            var access = GetCurrentUserAccess();
            var meeting = await GetMeetingWithReadAccess(id, access);

            if (meeting == null)
                return NotFound();

            var user = HttpContext.AuthenticatedUser()!;

            var member = await GetMeetingMember(meeting.Id, user.Id);
            if (member == null)
                return this.WorkingForbid("You need to join a meeting before voting in it");

            var poll = await database.MeetingPolls.FindAsync(id, pollId);

            if (poll == null)
                return NotFound();

            if (poll.ClosedAt != null)
                return BadRequest("The poll is closed");

            // TODO: add configuration for polls that allow empty votes
            if (request.SelectedOptions.Count < 1)
                return BadRequest("You need to select at least one option");

            // Fail if already voted
            if (await GetPollVotingRecord(meeting.Id, poll.PollId, user.Id) != null)
                return BadRequest("You have already voted in this poll");

            // Voting power is doubled if person is or has been a board member (as defined in the association rules)
            float votingPower = 1;

            if (user.HasBeenBoardMember)
                votingPower = 2;

            // These should execute within a single transaction so only one of these can't get through
            var votingRecord = new MeetingPollVotingRecord()
            {
                MeetingId = meeting.Id,
                PollId = poll.PollId,
                UserId = user.Id,
            };

            var vote = new MeetingPollVote()
            {
                MeetingId = meeting.Id,
                PollId = poll.PollId,
                VotingPower = votingPower,
                ParsedVoteContent = request,

                // TODO: when this is a board meeting the president's vote is the deciding factor.
                // In a meeting the chairman's vote resolves a tiebreak (this is currently assumed)
                IsTiebreaker = user.Id == meeting.OwnerId,
            };

            await database.MeetingPollVotingRecords.AddAsync(votingRecord);
            await database.MeetingPollVotes.AddAsync(vote);

            await database.SaveChangesAsync();
            logger.LogInformation("User {Email} has voted in poll {Id} at {UtcNow}", user.Email, poll.PollId,
                DateTime.UtcNow);

            return Ok();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
        [HttpPost("{id:long}/polls")]
        public async Task<IActionResult> CreatePoll([Required] long id, [Required] [FromBody] MeetingPollDTO request)
        {
            PollData parsedData;

            try
            {
                parsedData = request.ParsedData;
            }
            catch (Exception e)
            {
                logger.LogWarning("Exception when parsing new meeting poll data: {@E}", e);
                return BadRequest("Invalid poll data");
            }

            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(parsedData, new ValidationContext(parsedData), errors))
            {
                logger.LogError("New poll data didn't pass validation:");

                foreach (var error in errors)
                    logger.LogError("Failure: {Error}", error);

                // TODO: send errors to client?
                return BadRequest("Invalid poll JSON data");
            }

            // Check poll choice ids
            foreach (var pair in parsedData.Choices)
            {
                if (pair.Key != pair.Value.Id)
                    return BadRequest("Mismatch in option ID and Dictionary key");
            }

            // Detect duplicate names
            if (parsedData.Choices.GroupBy(p => p.Value.Name).Any(x => x.Count() > 1))
            {
                return BadRequest("Duplicate option name in poll");
            }

            if (parsedData.WeightedChoices != null)
            {
                // Weighted type poll
            }

            var access = GetCurrentUserAccess();
            var meeting = await GetMeetingWithReadAccess(id, access);

            if (meeting == null)
                return NotFound();

            var user = HttpContext.AuthenticatedUser()!;

            var member = await GetMeetingMember(meeting.Id, user.Id);
            if (member == null)
                return this.WorkingForbid("You need to join a meeting before creating polls in it");

            if (meeting.OwnerId != user.Id && !user.HasAccessLevel(UserAccessLevel.Admin))
                return this.WorkingForbid("You don't have permission to create polls in this meeting");

            if (request.AutoCloseAt < DateTime.UtcNow + TimeSpan.FromMinutes(1))
            {
                return BadRequest("Poll can't auto-close in less than a minute");
            }

            // TODO: check that there isn't a poll with the same title already

            var previousPollId = await database.MeetingPolls.Where(p => p.MeetingId == meeting.Id)
                .MaxAsync(p => (long?)p.PollId) ?? 0;

            var poll = new MeetingPoll
            {
                MeetingId = meeting.Id,
                PollId = ++previousPollId,
                Title = request.Title,
                TiebreakType = request.TiebreakType,
                AutoCloseAt = request.AutoCloseAt,
                ParsedData = parsedData,
            };

            await database.MeetingPolls.AddAsync(poll);

            await database.ActionLogEntries.AddAsync(new ActionLogEntry()
            {
                Message = $"Poll added to meeting ({meeting.Id}) with title: {poll.Title}",
                PerformedById = user.Id,
            });

            await database.SaveChangesAsync();
            logger.LogInformation("New poll {Id1} ({Title}) added to meeting ({Id2}) by {Email}", poll.PollId,
                poll.Title, meeting.Id, user.Email);

            if (poll.AutoCloseAt != null)
            {
                // Queue job to close the poll
                jobClient.Schedule<CloseAutoClosePollJob>(
                    x => x.Execute(meeting.Id, poll.PollId, CancellationToken.None), poll.AutoCloseAt.Value);
            }

            return Ok();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
        [HttpPost("{id:long}/polls/{pollId:long}/recompute")]
        public async Task<IActionResult> RefreshPollResults([Required] long id, [Required] long pollId)
        {
            var access = GetCurrentUserAccess();
            var meeting = await GetMeetingWithReadAccess(id, access);

            if (meeting == null)
                return NotFound();

            var user = HttpContext.AuthenticatedUser()!;

            var poll = await database.MeetingPolls.FindAsync(id, pollId);

            if (poll == null)
                return NotFound();

            if (poll.ClosedAt == null)
                return BadRequest("The poll is not closed");

            if (DateTime.UtcNow - poll.PollResultsCreatedAt < TimeSpan.FromSeconds(30))
                return BadRequest("The poll has been (re)computed in the past 30 seconds");

            if (meeting.OwnerId != user.Id && !user.HasAccessLevel(UserAccessLevel.Admin))
                return this.WorkingForbid("You don't have permission to recompute polls in this meeting");

            jobClient.Enqueue<ComputePollResultsJob>(x => x.Execute(poll.MeetingId, poll.PollId,
                CancellationToken.None));

            logger.LogInformation("User {Email} has queued recompute for poll {Id} at {UtcNow}", user.Email,
                poll.PollId, DateTime.UtcNow);

            return Ok();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
        [HttpPost]
        public async Task<IActionResult> CreateNew([Required] [FromBody] MeetingDTO request)
        {
            if (request.ReadAccess > request.JoinAccess)
                return BadRequest("Read access must not be higher than join access");

            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length < 3 || request.Name.Length > 100)
                return BadRequest("Meeting name must be between 3 and 100 characters");

            if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length < 3 ||
                request.Description.Length > 10000)
            {
                return BadRequest("Description must be between 3 and 10000 characters");
            }

            if (request.ExpectedDuration != null && (request.ExpectedDuration.Value < TimeSpan.FromMinutes(1) ||
                    request.ExpectedDuration.Value > TimeSpan.FromMinutes(650)))
            {
                return BadRequest($"Invalid expected duration, got value: {request.ExpectedDuration}");
            }

            if (request.JoinGracePeriod < TimeSpan.FromSeconds(0) ||
                request.JoinGracePeriod > TimeSpan.FromMinutes(200))
            {
                return BadRequest("Invalid join grace period");
            }

            if (request.StartsAt <= DateTime.UtcNow + TimeSpan.FromMinutes(1))
                return BadRequest("Can't create a meeting that would have started already");

            var access = GetCurrentUserAccess();
            if (request.JoinAccess > access)
                return BadRequest("Can't create a meeting you couldn't join due to join restriction");

            if (await database.Meetings.FirstOrDefaultAsync(m => m.Name == request.Name) != null)
                return BadRequest("A meeting with that name already exists");

            var user = HttpContext.AuthenticatedUser()!;

            var meeting = new Meeting()
            {
                Name = request.Name,
                Description = request.Description,
                ReadAccess = request.ReadAccess,
                JoinAccess = request.JoinAccess,
                JoinGracePeriod = request.JoinGracePeriod,
                StartsAt = request.StartsAt,
                ExpectedDuration = request.ExpectedDuration,
                OwnerId = user.Id
            };

            await database.Meetings.AddAsync(meeting);

            await database.ActionLogEntries.AddAsync(new ActionLogEntry()
            {
                Message = $"New meeting ({meeting.Name}) created, scheduled to start at {meeting.StartsAt}",
                PerformedById = user.Id,
            });

            await database.SaveChangesAsync();

            logger.LogInformation("New meeting ({Id}) created by {Email}", meeting.Id, user.Email);

            return Ok();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
        [HttpPost("{id:long}/end")]
        public async Task<IActionResult> EndMeeting([Required] long id)
        {
            var access = GetCurrentUserAccess();
            var meeting = await GetMeetingWithReadAccess(id, access);

            if (meeting == null)
                return NotFound();

            var user = HttpContext.AuthenticatedUser()!;

            if (meeting.OwnerId != user.Id && !user.HasAccessLevel(UserAccessLevel.Admin))
                return this.WorkingForbid("You don't have permission to end this meeting");

            if (meeting.EndedAt != null)
            {
                return BadRequest("The meeting has already been ended");
            }

            // Need to close still open polls
            var pollsToClose = await database.MeetingPolls.Where(p => p.MeetingId == meeting.Id && p.ClosedAt == null)
                .ToListAsync();

            foreach (var poll in pollsToClose)
            {
                logger.LogInformation("Closing a poll due to meeting closing: {Id}-{PollId}", poll.MeetingId,
                    poll.PollId);

                poll.ClosedAt = DateTime.UtcNow;

                // Queue a job to calculate the results
                jobClient.Schedule<ComputePollResultsJob>(x => x.Execute(poll.MeetingId, poll.PollId,
                    CancellationToken.None), TimeSpan.FromSeconds(15));
            }

            await database.ActionLogEntries.AddAsync(new ActionLogEntry()
            {
                Message = $"Meeting ({meeting.Id}) ended by a user",
                PerformedById = user.Id,
            });

            meeting.EndedAt = DateTime.UtcNow;

            await database.SaveChangesAsync();
            logger.LogInformation("Meeting {Id} has been ended by {Email}", meeting.Id, user.Email);

            return Ok();
        }

        [NonAction]
        private AssociationResourceAccess GetCurrentUserAccess()
        {
            var user = HttpContext.AuthenticatedUser();

            var access = AssociationResourceAccess.Public;

            if (user != null)
                access = user.ComputeAssociationAccessLevel();
            return access;
        }

        [NonAction]
        private async Task<Meeting?> GetMeetingWithReadAccess(long id, AssociationResourceAccess access)
        {
            var meeting = await database.Meetings.FindAsync(id);

            if (meeting == null || meeting.ReadAccess > access)
                return null;

            return meeting;
        }

        [NonAction]
        private Task<MeetingMember?> GetMeetingMember(long meetingId, long userId)
        {
            return database.MeetingMembers.FirstOrDefaultAsync(m => m.MeetingId == meetingId && m.UserId == userId);
        }

        [NonAction]
        private Task<MeetingPollVotingRecord?> GetPollVotingRecord(long meetingId, long pollId, long userId)
        {
            return database.MeetingPollVotingRecords.FirstOrDefaultAsync(r =>
                r.MeetingId == meetingId && r.PollId == pollId && r.UserId == userId);
        }
    }
}
