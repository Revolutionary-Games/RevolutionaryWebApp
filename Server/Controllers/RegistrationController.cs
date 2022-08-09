using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers;

using System.Threading.Tasks;
using Authorization;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared.Forms;
using Shared.Models;

[ApiController]
[Route("api/v1/[controller]")]
public class RegistrationController : Controller
{
    private readonly ILogger<RegistrationController> logger;
    private readonly IRegistrationStatus configuration;
    private readonly ITokenVerifier csrfVerifier;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public RegistrationController(ILogger<RegistrationController> logger, IRegistrationStatus configuration,
        ITokenVerifier csrfVerifier, NotificationsEnabledDb database, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.csrfVerifier = csrfVerifier;
        this.database = database;
        this.jobClient = jobClient;
    }

    /// <summary>
    ///   Returns true if registration is enabled
    /// </summary>
    [HttpGet]
    public bool Get()
    {
        return configuration.RegistrationEnabled;
    }

    [HttpPost]
    public async Task<IActionResult> Post(RegistrationFormData request)
    {
        if (!csrfVerifier.IsValidCSRFToken(request.CSRF, null, false))
            return BadRequest("Invalid CSRF");

        if (!SecurityHelpers.SlowEquals(request.RegistrationCode, configuration.RegistrationCode))
            return BadRequest("Invalid registration code");

        if (!request.Email.Contains('@'))
            return BadRequest("Email is invalid");

        // Check for conflicting username or email
        if (await database.Users.FirstOrDefaultAsync(u => u.UserName == request.Name) != null ||
            await database.Users.FirstOrDefaultAsync(u => u.Email == request.Email) != null)
        {
            return BadRequest("There is already an account associated with the given email or name");
        }

        var password = Passwords.CreateSaltedPasswordHash(request.Password);

        var user = new User()
        {
            Email = request.Email,
            UserName = request.Name,
            PasswordHash = password,
            Local = true,
        };

        await database.Users.AddAsync(user);
        Models.User.OnNewUserCreated(user, jobClient);
        await database.SaveChangesAsync();

        logger.LogInformation("New user registered {Name} ({Email})", request.Name, request.Email);

        return Created($"/users/{user.Id}", user.GetInfo(RecordAccessLevel.Private));
    }
}