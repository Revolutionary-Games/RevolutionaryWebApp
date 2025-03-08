namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using DevCenterCommunication.Models;
using Filters;
using Hangfire;
using Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared;
using Shared.Forms;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class CLAController : Controller
{
    private readonly ILogger<CLAController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly ICLASignatureStorage signatureStorage;
    private readonly IMailQueue mailQueue;
    private readonly IBackgroundJobClient jobClient;
    private readonly string? emailSignaturesTo;

    public CLAController(ILogger<CLAController> logger, IConfiguration configuration,
        NotificationsEnabledDb database, ICLASignatureStorage signatureStorage, IMailQueue mailQueue,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.signatureStorage = signatureStorage;
        this.mailQueue = mailQueue;
        this.jobClient = jobClient;

        emailSignaturesTo = configuration["CLA:SignatureEmailBCC"];
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpGet]
    public async Task<PagedResult<CLAInfo>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<Cla> query;

        try
        {
            query = database.Clas.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetInfo());
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<CLADTO>> GetSingle([Required] long id)
    {
        var cla = await database.Clas.FindAsync(id);

        if (cla == null)
            return NotFound();

        if (!cla.Active && HttpContext.HasAuthenticatedUserWithGroup(GroupType.Admin, null) != true)
            return this.WorkingForbid("Only admins can view non-active CLAs");

        return cla.GetDTO();
    }

    [HttpGet("active")]
    public async Task<ActionResult<CLADTO>> GetActive()
    {
        var cla = await database.Clas.Where(c => c.Active).FirstOrDefaultAsync();

        if (cla == null)
            return NotFound();

        return cla.GetDTO();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpPost]
    public async Task<IActionResult> CreateNew([Required] [FromBody] CLADTO request)
    {
        var newCla = new Cla
        {
            Active = request.Active,
            RawMarkdown = request.RawMarkdown,
        };

        bool inactivated = false;

        // Other active CLAs need to become inactive if new one is added
        if (newCla.Active)
        {
            foreach (var cla in await database.Clas.Where(c => c.Active).ToListAsync())
            {
                cla.Active = false;
                inactivated = true;
                logger.LogInformation("CLA {Id} is being made inactive due to creating a new one", cla.Id);
            }
        }

        var user = HttpContext.AuthenticatedUser()!;

        await database.AdminActions.AddAsync(new AdminAction($"New CLA with active status: {newCla.Active} created")
        {
            PerformedById = user.Id,
        });

        await database.Clas.AddAsync(newCla);
        await database.SaveChangesAsync();

        logger.LogInformation("New CLA {Id} with active: {Active} created by {Email}", newCla.Id,
            newCla.Active, user.Email);

        if (inactivated)
        {
            jobClient.Enqueue<InvalidatePullRequestsWithCLASignaturesJob>(x => x.Execute(CancellationToken.None));
        }

        return Ok();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpPost("{id}/activate")]
    public async Task<IActionResult> Activate([Required] long id)
    {
        var cla = await database.Clas.FindAsync(id);

        if (cla == null)
            return NotFound();

        if (cla.Active)
            return BadRequest("Already active");

        // Other active CLAs need to become inactive
        foreach (var otherCla in await database.Clas.Where(c => c.Active).ToListAsync())
        {
            otherCla.Active = false;
            logger.LogInformation("CLA {Id} is being made inactive due to activating {Id2}", otherCla.Id,
                cla.Id);
        }

        cla.Active = true;
        await database.SaveChangesAsync();

        logger.LogInformation("CLA {Id} activated by {Email}", cla.Id,
            HttpContext.AuthenticatedUser()!.Email);

        jobClient.Enqueue<InvalidatePullRequestsWithCLASignaturesJob>(x => x.Execute(CancellationToken.None));

        return Ok();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
            HttpContext.AuthenticatedUser()!.Email);

        jobClient.Enqueue<InvalidatePullRequestsWithCLASignaturesJob>(x => x.Execute(CancellationToken.None));

        return Ok();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    [HttpGet("{id:long}/search")]
    public async Task<ActionResult<List<CLASignatureSearchResult>>> SearchSignatures([Required] long id,
        string? email, string? githubAccount)
    {
        IQueryable<ClaSignature> query = database.ClaSignatures.Where(s => s.ClaId == id);

        bool allowEmailInResult = false;
        bool allowGithubInResult = false;

        if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(githubAccount))
        {
            // Both search criteria

            if (email.Length >= AppInfo.PartialEmailMatchRevealAfterLength)
                allowEmailInResult = true;

            if (githubAccount.Length >= AppInfo.PartialGithubMatchRevealAfterLength)
                allowGithubInResult = true;

            query = query.Where(s => s.Email == email || s.GithubAccount == githubAccount);
        }
        else if (!string.IsNullOrEmpty(email))
        {
            allowEmailInResult = true;
            query = query.Where(s => s.Email == email);
        }
        else if (!string.IsNullOrEmpty(githubAccount))
        {
            allowGithubInResult = true;
            query = query.Where(s => s.GithubAccount == githubAccount);
        }
        else
        {
            return BadRequest("both search criteria can't be empty");
        }

        var data = await query.OrderBy(s => s.Id).Take(10).ToListAsync();

        return data.Select(s => s.ToSearchResult(s.Email == email || (allowEmailInResult && s.Email.Contains(email!)),
                s.GithubAccount == githubAccount ||
                (allowGithubInResult && s.GithubAccount != null && s.GithubAccount.Contains(githubAccount!))))
            .ToList();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpGet("signatures")]
    public async Task<PagedResult<CLASignatureDTO>> GetSignatures([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<ClaSignature> query;

        try
        {
            query = database.ClaSignatures.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    /// <summary>
    ///   Checks a bulk data blob for CLA signatures
    /// </summary>
    /// <param name="request">The request data</param>
    /// <returns>
    ///   List of either signed or not signed people in the request, depending on the flags in the request
    /// </returns>
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpPost("checkSignatures")]
    public async Task<ActionResult<List<string>>> BulkCheckSignatures([Required] [FromBody] BulkCLACheckRequest request)
    {
        var cla = await database.Clas.Where(c => c.Active).FirstOrDefaultAsync();

        if (cla == null)
            return BadRequest("There is no active CLA to perform the check with");

        var rawList = request.ItemsToCheck.Split('\n').Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();

        if (rawList.Count < 1)
            return BadRequest("No items to check provided");

        if (rawList.Count > AppInfo.MaxBulkCLAChecksPerCall)
            return BadRequest($"Too many items to check at once. Maximum is: {AppInfo.MaxBulkCLAChecksPerCall}");

        var found = new List<string>();

        switch (request.CheckType)
        {
            case CLACheckRequestType.Email:
                foreach (var email in rawList)
                {
                    if (await database.ClaSignatures.FirstOrDefaultAsync(s =>
                            s.ValidUntil == null && s.ClaId == cla.Id && s.Email == email) != null)
                    {
                        found.Add(email);
                    }
                }

                break;
            case CLACheckRequestType.GithubUsername:
                foreach (var github in rawList)
                {
                    if (await database.ClaSignatures.FirstOrDefaultAsync(s =>
                            s.ValidUntil == null && s.ClaId == cla.Id && s.GithubAccount == github) != null)
                    {
                        found.Add(github);
                    }
                }

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (request.ReturnNotFound)
        {
            return rawList.Where(i => !found.Contains(i)).ToList();
        }

        return found;
    }

    [HttpPost("startSigning")]
    public async Task<ActionResult<SigningStartResponse>> StartSigning([Required] long id)
    {
        logger.LogInformation("Received a request to start signing CLA: {Id}", id);

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
            var signature = new InProgressClaSignature
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
            var signature =
                await database.InProgressClaSignatures.FirstOrDefaultAsync(s => s.SessionId == session.Id);
            if (signature == null || signature.ClaId != id)
            {
                if (signature != null)
                {
                    // Need to delete old in progress sign for wrong CLA version
                    database.InProgressClaSignatures.Remove(signature);
                }

                signature = new InProgressClaSignature
                {
                    SessionId = session.Id,
                    ClaId = id,
                };

                await database.InProgressClaSignatures.AddAsync(signature);
            }
        }

        session.LastUsedFrom = remoteAddress;

        await database.SaveChangesAsync();

        if (createdNew)
            LoginController.SetSessionCookie(session, Response);

        return new SigningStartResponse
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

        var signature = await database.InProgressClaSignatures.FirstOrDefaultAsync(s => s.SessionId == session.Id);

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

        var signature = await database.InProgressClaSignatures.FirstOrDefaultAsync(s => s.SessionId == session.Id);

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

        var signature = await database.InProgressClaSignatures.FirstOrDefaultAsync(s => s.SessionId == session.Id);

        if (signature == null)
            return NotFound();

        // Update allowed fields from request to the data of the in-progress signature stored in the database
        bool changes = false;

        if (request.GithubSkipped != signature.GithubSkipped)
        {
            changes = true;
            signature.GithubSkipped = request.GithubSkipped;

            if (signature.GithubSkipped)
            {
                signature.GithubAccount = null;
                signature.GithubEmail = null;
                signature.GithubUserId = null;
            }
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
        {
            signature.BumpUpdatedAt();
            await database.SaveChangesAsync();
        }

        return Ok();
    }

    [HttpPost("finishSigning")]
    public async Task<IActionResult> CompleteSignature([FromBody] [Required] InProgressClaSignatureDTO request)
    {
        var session = await HttpContext.Request.Cookies.GetSession(database);

        if (session == null)
            return this.WorkingForbid("You don't have an active session");

        var signature = await database.InProgressClaSignatures.FirstOrDefaultAsync(s => s.SessionId == session.Id);

        if (signature == null)
            return NotFound();

        if (!signatureStorage.Configured)
            return Problem("Remote storage for signed documents is not configured");

        if (!mailQueue.Configured)
            return Problem("Email on server is not configured");

        // Request must now have signer and guardian (if minor) signature fields set (all other changes are
        // ignored)
        if (string.IsNullOrWhiteSpace(request.SignerSignature) || request.SignerSignature.Length < 3)
            return BadRequest("Missing signature");

        if (!signature.SignerIsMinor.HasValue)
            return BadRequest("Is minor field is not filled in");

        if (signature.SignerIsMinor == true && (string.IsNullOrWhiteSpace(request.GuardianSignature) ||
                request.GuardianSignature.Length < 3))
        {
            return BadRequest("Missing guardian signature");
        }

        // Verify that signature status is fine before moving onto accepting it
        if (!signature.EmailVerified || string.IsNullOrWhiteSpace(signature.Email))
            return BadRequest("Email is not verified");

        if (!signature.GithubSkipped && (string.IsNullOrWhiteSpace(signature.GithubAccount)
                || string.IsNullOrEmpty(signature.GithubEmail) || !signature.GithubUserId.HasValue))
        {
            return BadRequest("Bad Github account status in signature");
        }

        if (signature.GithubSkipped && signature.GithubAccount != null)
            return BadRequest("Bad Github account status in signature");

        if (signature.DeveloperUsername != null && string.IsNullOrWhiteSpace(signature.DeveloperUsername))
            return BadRequest("Bad format username");

        if (signature.SignerIsMinor == true && (string.IsNullOrWhiteSpace(request.GuardianName) ||
                request.GuardianName.Length < 3))
        {
            return BadRequest("Missing guardian name");
        }

        if (string.IsNullOrWhiteSpace(request.SignerName) || request.SignerName.Length < 3)
            return BadRequest("Missing signer name");

        // Load the CLA data (it must still be active, otherwise signing is not valid)
        var cla = await database.Clas.FindAsync(signature.ClaId);

        if (cla is not { Active: true })
            return BadRequest("CLA document you are trying to sign was not found or it is no longer active");

        var user = HttpContext.AuthenticatedUser();
        var userId = user?.Id;

        // Fail if there's already exactly this signature created
        if (await database.ClaSignatures.FirstOrDefaultAsync(s => s.ClaId == cla.Id && s.Email == signature.Email &&
                s.GithubAccount == signature.GithubAccount &&
                s.DeveloperUsername == signature.DeveloperUsername &&
                s.UserId == userId && s.ValidUntil == null) != null)
        {
            return BadRequest("A CLA has already been signed with the details you provided. Signing it multiple " +
                "times is not required");
        }

        signature.SignerSignature = request.SignerSignature;
        signature.GuardianSignature = request.GuardianSignature;

        // Create the actual signature database entry
        var finalSignature = new ClaSignature
        {
            Email = signature.Email,
            GithubAccount = signature.GithubAccount,
            GithubEmail = signature.GithubEmail,
            GithubUserId = signature.GithubUserId,
            DeveloperUsername = signature.DeveloperUsername,
            ClaId = cla.Id,
            UserId = userId,
        };

        var fileName = $"{signature.Email}-{finalSignature.CreatedAt:dd-HH:mm:ss}.md";

        finalSignature.ClaSignatureStoragePath = $"signatures/cla/{finalSignature.CreatedAt:yyyy-MM}/{fileName}";

        // Save changes here to ensure that the created storage path is unique
        await database.ClaSignatures.AddAsync(finalSignature);
        await database.SaveChangesAsync();

        // Prepare the final text
        var signedDocumentText = CreateSignedDocumentText(cla, signature, finalSignature, user);

        // This seems the most failure-prone thing here, so this is done first to then have smooth sailing afterwards
        int attempts = 10;
        while (true)
        {
            --attempts;

            try
            {
                // Upload the signature to remote storage
                await signatureStorage.UploadFile(finalSignature.ClaSignatureStoragePath,
                    new MemoryStream(Encoding.UTF8.GetBytes(signedDocumentText)),
                    AppInfo.MarkdownMimeType, true, CancellationToken.None);

                break;
            }
            catch (Exception e)
            {
                if (attempts > 0)
                {
                    logger.LogWarning("Retrying upload of signed document to remote storage");
                    await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);
                    continue;
                }

                logger.LogError(e, "Failed to upload signed document to remote storage (will delete signature)");

                // Need to delete the signature to not fail to sign it again
                database.ClaSignatures.Remove(finalSignature);
                await database.SaveChangesAsync();

                return Problem("Failed to upload signed document to remote storage. Please try again.");
            }
        }

        // TODO: switch signing to require a devcenter account once anyone can register (and the type below to action)
        logger.LogInformation(
            "CLA ({Id1}) signature created with ID {Id2}, with email: {Email} from: {RemoteIpAddress}", cla.Id,
            finalSignature.Id, finalSignature.Email, HttpContext.Connection.RemoteIpAddress);
        await database.LogEntries.AddAsync(new LogEntry($"New CLA signature for CLA ({cla.Id}) created")
        {
            TargetUserId = finalSignature.UserId,
        });

        // Email the agreement to the person signing it
        var emailTask = mailQueue.SendEmail(
            new MailRequest(finalSignature.Email, "Your signed document from ThriveDevCenter")
            {
                Bcc = emailSignaturesTo,
                PlainTextBody = "Here is the document you just signed on ThriveDevCenter (as an attachment)",
                HtmlBody = "<p>Here is the document you just signed on ThriveDevCenter (as an attachment)</p>",
                Attachments = new List<MailAttachment>
                {
                    new(fileName, signedDocumentText)
                    {
                        MimeType = AppInfo.MarkdownMimeType,
                    },
                },
            }, CancellationToken.None);

        // Delete the in-progress signature
        database.InProgressClaSignatures.Remove(signature);
        await database.SaveChangesAsync();

        await emailTask;

        if (!string.IsNullOrEmpty(finalSignature.GithubAccount))
        {
            logger.LogInformation("Got CLA with a github username, will re-check pull requests by {GithubAccount}",
                finalSignature.GithubAccount);

            // Maybe added delay here will fix something?
            jobClient.Schedule<CheckPullRequestsAfterNewSignatureJob>(x =>
                x.Execute(finalSignature.GithubAccount, CancellationToken.None), TimeSpan.FromSeconds(15));
        }
        else
        {
            logger.LogInformation("Signature with no GitHub account made");
        }

        return Ok();
    }

    [NonAction]
    private string CreateSignedDocumentText(Cla cla, InProgressClaSignature signature, ClaSignature finalSignature,
        User? user)
    {
        var signedBuilder = new StringBuilder(cla.RawMarkdown.Length * 2);
        signedBuilder.Append(cla.RawMarkdown);
        signedBuilder.Append("\n\n---\n\nThis document is digitally signed on ThriveDevCenter by:\n");

        signedBuilder.Append(signature.SignerName);
        signedBuilder.Append('\n');
        signedBuilder.Append('\n');
        signedBuilder.Append("Signature entered as: ");
        signedBuilder.Append(signature.SignerSignature);
        signedBuilder.Append('\n');
        signedBuilder.Append('\n');

        if (signature.SignerIsMinor == true)
        {
            signedBuilder.Append("Signer is a minor / doesn't have capacity to sign by themselves.\n");
            signedBuilder.Append("Guardian: ");
            signedBuilder.Append(signature.GuardianName);
            signedBuilder.Append('\n');
            signedBuilder.Append('\n');

            signedBuilder.Append("Guardian's signature entered as: ");
            signedBuilder.Append(signature.GuardianSignature);
            signedBuilder.Append('\n');
            signedBuilder.Append('\n');
        }

        signedBuilder.Append("Provided username on signing form: ");
        signedBuilder.Append(finalSignature.DeveloperUsername);
        signedBuilder.Append('\n');

        signedBuilder.Append("\n---\n\n");

        signedBuilder.Append("The following information is created or verified by the server:\n\n");

        signedBuilder.Append("Email associated with this signature: ");
        signedBuilder.Append(finalSignature.Email);
        signedBuilder.Append('\n');
        signedBuilder.Append('\n');

        signedBuilder.Append("Github username associated with this signature: ");
        signedBuilder.Append(finalSignature.GithubAccount);
        signedBuilder.Append(" (Github id: ");
        signedBuilder.Append(finalSignature.GithubUserId?.ToString() ?? "unknown");
        signedBuilder.Append(")");
        signedBuilder.Append('\n');
        signedBuilder.Append('\n');

        if (!string.IsNullOrEmpty(finalSignature.GithubEmail))
        {
            signedBuilder.Append("Email associated with the Github account: ");
            signedBuilder.Append(finalSignature.GithubEmail);
            signedBuilder.Append('\n');
            signedBuilder.Append('\n');
        }

        signedBuilder.Append("This signature was created at ");
        signedBuilder.Append(finalSignature.CreatedAt.ToString("O"));
        signedBuilder.Append('\n');
        signedBuilder.Append('\n');

        signedBuilder.Append("Signing this document was started at ");
        signedBuilder.Append(signature.CreatedAt.ToString("O"));
        signedBuilder.Append('\n');
        signedBuilder.Append('\n');

        signedBuilder.Append("The following ID number has been given to this signature: **");
        signedBuilder.Append(finalSignature.Id);
        signedBuilder.Append("**\n");
        signedBuilder.Append('\n');

        if (user != null)
        {
            signedBuilder.Append("The following ThriveDevCenter account was used to perform this signature: ");
            signedBuilder.Append(user.Email);
            signedBuilder.Append(" (");
            signedBuilder.Append(user.Id);
            signedBuilder.Append(")\n");
            signedBuilder.Append('\n');
        }

        signedBuilder.Append('\n');
        signedBuilder.Append("Signature request received from IP address: ");
        signedBuilder.Append(HttpContext.Connection.RemoteIpAddress);
        signedBuilder.Append('\n');

        return signedBuilder.ToString();
    }
}
