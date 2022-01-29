using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Threading;
    using System.Threading.Tasks;
    using Authorization;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared.Forms;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class EmailVerificationController : Controller
    {
        private readonly ILogger<EmailVerificationController> logger;
        private readonly NotificationsEnabledDb database;
        private readonly EmailTokens emailTokens;
        private readonly IMailSender mailSender;
        private readonly Uri baseUrl;

        public EmailVerificationController(ILogger<EmailVerificationController> logger, IConfiguration configuration,
            NotificationsEnabledDb database, EmailTokens emailTokens, IMailSender mailSender)
        {
            this.logger = logger;
            this.database = database;
            this.emailTokens = emailTokens;
            this.mailSender = mailSender;

            baseUrl = configuration.GetBaseUrl();
        }

        [HttpPost]
        public async Task<ActionResult<EmailVerifyResult>> StartEmailVerifyForCLA(
            [Required] [FromBody] EmailVerificationFinishForm request)
        {
            var verifiedToken = emailTokens.ReadAndVerify(request.Token);

            if (verifiedToken == null)
            {
                return this.WorkingForbid(
                    "Invalid email token given. Please check you used the right link and " +
                    "that it didn't expire yet");
            }

            const string sameBrowserAdvice =
                "Make sure to use the link in the same browser where you started email verification from.";

            string redirect;

            switch (verifiedToken.Type)
            {
                case EmailVerificationType.CLA:
                {
                    var (inProgressSign, session, error) = await GetActiveSignature();

                    if (error != null)
                    {
                        return this.WorkingForbid("You don't have an in-progress signature or session. " +
                            sameBrowserAdvice);
                    }

                    if (!SecurityHelpers.SlowEquals(session!.HashedId, verifiedToken.VerifiedResourceId))
                    {
                        return this.WorkingForbid("Current session doesn't match one the email token " +
                            "was sent from. " + sameBrowserAdvice);
                    }

                    // Valid CLA verified email
                    logger.LogInformation("Email verification of {Email} succeeded for CLA in session {Id}",
                        verifiedToken.SentToEmail, session.Id);

                    inProgressSign!.EmailVerified = true;
                    inProgressSign.Email = verifiedToken.SentToEmail;

                    redirect = "/cla/sign";
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            await database.SaveChangesAsync();

            return new EmailVerifyResult()
            {
                RedirectTo = redirect,
            };
        }

        [HttpPost("start/cla")]
        public async Task<ActionResult> StartEmailVerifyForCLA(
            [Required] [FromBody] EmailVerificationRequestForm request)
        {
            var (inProgressSign, session, error) = await GetActiveSignature();

            if (error != null)
                return error;

            if (inProgressSign!.EmailVerified && inProgressSign.Email == request.Email)
                return BadRequest("That email has already been verified");

            var token = emailTokens.GenerateToken(new EmailTokenData()
            {
                SentToEmail = request.Email,
                Type = EmailVerificationType.CLA,

                // TODO: make the hashed id a required field and remove the exception here
                VerifiedResourceId = session!.HashedId ?? throw new Exception("hashed id not calculated for a session"),
            });

            logger.LogInformation("Beginning verification email send to {Email} by client from {RemoteIpAddress}",
                request.Email, HttpContext.Connection.RemoteIpAddress);

            var returnUrl = new Uri(baseUrl, $"/verify/email?token={token}").ToString();

            await mailSender.SendEmail(new MailRequest(request.Email, "ThriveDevCenter Email Verification")
            {
                PlainTextBody = "Someone (hopefully you) has requested to use your email in signing a document.\n" +
                    "If this was you, please copy the below link into your browser to verify your email: \n" +
                    returnUrl + "\n" +
                    "If you did not request your email to be used, then please ignore this email and DO NOT give the " +
                    "link to anyone.",
                HtmlBody = "<p>Someone (hopefully you) has requested to use your email in signing a document.</p>" +
                    "<p>If this was you, please click the below link to verify your email: <a href=\"" + returnUrl +
                    "\">" + returnUrl + "</a></p>" +
                    "<p>If you did not request your email to be used, then please ignore this email and " +
                    "<strong>DO NOT</strong> give the link to anyone.</p>",
            }, CancellationToken.None);

            return Ok();
        }

        [NonAction]
        private async Task<(InProgressClaSignature?, Session?, ActionResult?)> GetActiveSignature()
        {
            var session = await HttpContext.Request.Cookies.GetSession(database);

            if (session == null)
                return (null, null, this.WorkingForbid("You don't have an active session"));

            var inProgressSign =
                await database.InProgressClaSignatures.FirstOrDefaultAsync(s => s.SessionId == session.Id);

            if (inProgressSign == null)
                return (null, null, this.WorkingForbid("You don't have an in-progress signature"));

            return (inProgressSign, session, null);
        }
    }
}
