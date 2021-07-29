using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Authorization;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared.Forms;
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

        [HttpPost("cla")]
        public async Task<IActionResult> StartEmailVerifyForCLA(
            [Required] [FromBody] EmailVerificationRequestForm request)
        {
            var session = await HttpContext.Request.Cookies.GetSession(database);

            if (session == null)
                return this.WorkingForbid("You don't have an active session");

            var inProgressSign =
                await database.InProgressClaSignatures.FirstOrDefaultAsync(s => s.SessionId == session.Id);

            if (inProgressSign == null)
                return this.WorkingForbid("You don't have an in-progress signature");

            var token = emailTokens.GenerateToken(new EmailTokenData()
            {
                SentToEmail = request.Email,
                Type = EmailVerificationType.CLA,
                VerifiedResourceId = session.HashedId,
            });

            logger.LogInformation("Beginning verification email send to {Email} by client from {RemoteIpAddress}",
                request.Email, HttpContext.Connection.RemoteIpAddress);

            var returnUrl = new Uri(baseUrl, $"/verify/email?token={token}").ToString();

            await mailSender.SendEmail(new MailRequest()
            {
                Recipient = request.Email,
                Subject = "ThriveDevCenter Email Verification",
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
    }
}
