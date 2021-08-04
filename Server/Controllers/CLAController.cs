using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Threading.Tasks;
    using Authorization;
    using BlazorPagination;
    using Filters;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class CLAController : Controller
    {
        private readonly ILogger<CLAController> logger;
        private readonly NotificationsEnabledDb database;

        public CLAController(ILogger<CLAController> logger,
            NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpGet]
        public async Task<PagedResult<CLAInfo>> Get([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 100)] int pageSize)
        {
            IQueryable<Cla> query;

            try
            {
                query = database.Clas.AsQueryable().OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetInfo());
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpGet("{id:long}")]
        public async Task<ActionResult<CLADTO>> GetSingle([Required] long id)
        {
            var cla = await database.Clas.FindAsync(id);

            if (cla == null)
                return NotFound();

            return cla.GetDTO();
        }

        [HttpGet("active")]
        public async Task<ActionResult<CLADTO>> GetActive()
        {
            var cla = await database.Clas.AsQueryable().Where(c => c.Active).FirstOrDefaultAsync();

            if (cla == null)
                return NotFound();

            return cla.GetDTO();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpPost]
        public async Task<IActionResult> CreateNew([Required] [FromBody] CLADTO request)
        {
            var newCla = new Cla()
            {
                Active = request.Active,
                RawMarkdown = request.RawMarkdown,
            };

            // Other active CLAs need to become inactive if new one is added
            if (newCla.Active)
            {
                foreach (var cla in await database.Clas.AsQueryable().Where(c => c.Active).ToListAsync())
                {
                    cla.Active = false;
                    logger.LogInformation("CLA {Id} is being made inactive due to creating a new one", cla.Id);
                }
            }

            await database.Clas.AddAsync(newCla);
            await database.SaveChangesAsync();

            logger.LogInformation("New CLA {Id} with active: {Active} created by {Email}", newCla.Id,
                newCla.Active, HttpContext.AuthenticatedUser().Email);

            return Ok();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpPost("{id}/activate")]
        public async Task<IActionResult> Activate([Required] long id)
        {
            var cla = await database.Clas.FindAsync(id);

            if (cla == null)
                return NotFound();

            if (cla.Active)
                return BadRequest("Already active");

            // Other active CLAs need to become inactive
            foreach (var otherCla in await database.Clas.AsQueryable().Where(c => c.Active).ToListAsync())
            {
                otherCla.Active = false;
                logger.LogInformation("CLA {Id} is being made inactive due to activating {Id2}", otherCla.Id,
                    cla.Id);
            }

            cla.Active = true;
            await database.SaveChangesAsync();

            logger.LogInformation("CLA {Id} activated by {Email}", cla.Id,
                HttpContext.AuthenticatedUser().Email);

            return Ok();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpPost("{id}/deactivate")]
        public async Task<IActionResult> Deactivate([Required] long id)
        {
            var cla = await database.Clas.FindAsync(id);

            if (cla == null)
                return NotFound();

            if (!cla.Active)
                return BadRequest("CLA is not active");

            cla.Active = false;
            await database.SaveChangesAsync();

            logger.LogInformation("CLA {Id} deactivated by {Email}", cla.Id,
                HttpContext.AuthenticatedUser().Email);

            return Ok();
        }

        [HttpPost("startSigning")]
        public async Task<ActionResult<SigningStartResponse>> StartSigning([Required] long id)
        {
            var cla = await database.Clas.FindAsync(id);

            if (cla == null)
                return NotFound();

            if (!cla.Active)
                return BadRequest("Can't sign non-active CLA");

            var remoteAddress = Request.HttpContext.Connection.RemoteIpAddress;

            // There needs to be a session for the in-progress signature to be stored
            var session = await HttpContext.Request.Cookies.GetSession(database);
            bool createdNew = false;

            if (session == null)
            {
                var signature = new InProgressClaSignature()
                {
                    ClaId = id,
                };

                session = new Session
                {
                    LastUsedFrom = remoteAddress,
                    InProgressClaSignature = signature,
                };
                createdNew = true;

                signature.Session = session;

                logger.LogInformation("Starting a new session for CLA signing");
                await database.Sessions.AddAsync(session);
                await database.InProgressClaSignatures.AddAsync(signature);
            }
            else
            {
                logger.LogInformation("Using an existing session for CLA signing");

                session.LastUsed = DateTime.UtcNow;

                // Don't delete any active signatures for the session as the user may have just gotten lost and pressed
                // the sign button again

                // But add a new one if missing
                var signature = await database.InProgressClaSignatures.AsQueryable()
                    .FirstOrDefaultAsync(s => s.SessionId == session.Id);
                if (signature == null || signature.ClaId != id)
                {
                    if (signature != null)
                    {
                        // Need to delete old in progress sign for wrong CLA version
                        database.InProgressClaSignatures.Remove(signature);
                    }

                    signature = new InProgressClaSignature()
                    {
                        SessionId = session.Id,
                        ClaId = id
                    };

                    await database.InProgressClaSignatures.AddAsync(signature);
                }
            }

            session.LastUsedFrom = remoteAddress;

            await database.SaveChangesAsync();

            if (createdNew)
                LoginController.SetSessionCookie(session, Response);

            return new SigningStartResponse()
            {
                SessionStarted = createdNew,
                NextPath = "/cla/sign",
            };
        }

        [HttpGet("activeSigning")]
        public async Task<ActionResult<InProgressClaSignatureDTO>> GetCurrentActiveSignature()
        {
            var session = await HttpContext.Request.Cookies.GetSession(database);

            if (session == null)
                return this.WorkingForbid("You don't have an active session");

            var signature = await database.InProgressClaSignatures.AsQueryable()
                .FirstOrDefaultAsync(s => s.SessionId == session.Id);

            if (signature == null)
                return NotFound();

            return signature.GetDTO();
        }

        [HttpDelete("activeSigning")]
        public async Task<IActionResult> CancelSigningProcess()
        {
            var session = await HttpContext.Request.Cookies.GetSession(database);

            if (session == null)
                return this.WorkingForbid("You don't have an active session");

            var signature = await database.InProgressClaSignatures.AsQueryable()
                .FirstOrDefaultAsync(s => s.SessionId == session.Id);

            if (signature == null)
                return NotFound();

            // Remove the in-progress signature
            database.InProgressClaSignatures.Remove(signature);

            // TODO: should the user's session be ended if they aren't logged in? Would need a hard refresh
            // in that case

            await database.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("activeSigning")]
        public async Task<IActionResult> UpdateSigningProgress([FromBody] [Required] InProgressClaSignatureDTO request)
        {
            var session = await HttpContext.Request.Cookies.GetSession(database);

            if (session == null)
                return this.WorkingForbid("You don't have an active session");

            var signature = await database.InProgressClaSignatures.AsQueryable()
                .FirstOrDefaultAsync(s => s.SessionId == session.Id);

            if (signature == null)
                return NotFound();

            // Update allowed fields from request to the data of the in-progress signature stored in the database
            bool changes = false;

            if (request.GithubSkipped != signature.GithubSkipped)
            {
                changes = true;
                signature.GithubSkipped = request.GithubSkipped;

                if (signature.GithubSkipped)
                    signature.GithubAccount = null;
            }

            if (request.DeveloperUsername != signature.DeveloperUsername)
            {
                changes = true;
                signature.DeveloperUsername = request.DeveloperUsername;

                if (string.IsNullOrWhiteSpace(signature.DeveloperUsername))
                    signature.DeveloperUsername = null;
            }

            if (request.GuardianName != signature.GuardianName)
            {
                changes = true;

                signature.GuardianName = request.GuardianName;
                if (string.IsNullOrWhiteSpace(signature.GuardianName))
                    signature.GuardianName = null;
            }

            if (request.SignerName != signature.SignerName)
            {
                changes = true;

                signature.SignerName = request.SignerName;
            }

            if (request.SignerIsMinor != signature.SignerIsMinor)
            {
                changes = true;

                signature.SignerIsMinor = request.SignerIsMinor;

                if (signature.SignerIsMinor != true)
                    signature.GuardianName = null;
            }

            if (changes)
                await database.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("finishSigning")]
        public async Task<IActionResult> CompleteSignature([FromBody] [Required] InProgressClaSignatureDTO request)
        {
            var session = await HttpContext.Request.Cookies.GetSession(database);

            if (session == null)
                return this.WorkingForbid("You don't have an active session");

            var signature = await database.InProgressClaSignatures.AsQueryable()
                .FirstOrDefaultAsync(s => s.SessionId == session.Id);

            if (signature == null)
                return NotFound();

            // Request must now have signer and guardian (if minor) signature fields set (all other changes are
            // ignored)

            // Verify that signature status is fine before moving onto accepting it

            return Problem("not implemented");
        }
    }
}
