namespace ThriveDevCenter.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Services;
using Shared.Forms;
using Shared.Models;

[ApiController]
[Route("api/v1/[controller]")]
public class MailController : Controller
{
    private readonly ILogger<MailController> logger;
    private readonly IMailSender mailSender;

    public MailController(ILogger<MailController> logger, IMailSender mailSender)
    {
        this.logger = logger;
        this.mailSender = mailSender;
    }

    [HttpGet]
    public IActionResult IsConfigured()
    {
        if (!mailSender.Configured)
            return BadRequest("Email is not configured");

        return Ok();
    }

    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    [HttpPost("test")]
    public async Task<IActionResult> SendTestMail([FromBody] [Required] EmailTestRequestForm request)
    {
        if (!mailSender.Configured)
            return BadRequest("Email is not configured");

        logger.LogInformation("Test email sent by {Email} to {Recipient}", HttpContext.AuthenticatedUser()!.Email,
            request.Recipient);

        try
        {
            await mailSender.SendEmail(new MailRequest(request.Recipient, "Test Email from ThriveDevCenter")
            {
                PlainTextBody =
                    "This is a test email from ThriveDevCenter.\n If you received this, then things are working.",
                HtmlBody =
                    "<p>This is a test email from ThriveDevCenter.</p>" +
                    "<p>If you received this, then things are working.</p>",
            }, CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError("Error when sending test email: {@E}", e);
            return Problem("Error sending test mail. See server logs for more details.");
        }

        return Ok();
    }
}