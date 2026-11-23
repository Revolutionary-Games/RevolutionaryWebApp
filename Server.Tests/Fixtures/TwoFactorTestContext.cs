namespace RevolutionaryWebApp.Server.Tests.Fixtures;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OtpNet;
using Server.Authorization;
using Server.Jobs;
using Server.Models;
using Server.Services;
using Shared.Models;
using Shared.Models.Enums;

public sealed class TwoFactorTestContext : IDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> options =
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();

    public TwoFactorTestContext()
    {
        Database = CreateDatabase();
        Database.Database.EnsureCreated();
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "BaseUrl", "http://localhost:5000/" },
            { "Login:Local:Enabled", "true" },
            { "Login:Github:ClientId", "client" },
            { "Login:Github:ClientSecret", "secret" },
            { "Login:CommunityForum:SsoSecret", "forum-secret" },
            { "Login:CommunityForum:BaseUrl", "https://community.example.com/" },
            { "Login:DevForum:SsoSecret", "developer-secret" },
            { "Login:DevForum:BaseUrl", "https://developer.example.com/" },
            { "Login:Patreon:ClientId", "patreon-client" },
            { "Login:Patreon:ClientSecret", "patreon-secret" },
            { "Login:Patreon:BaseUrl", "https://patreon.example.com/" },
        }).Build();
        MailQueue.Configured.Returns(true);
        MailSender.Configured.Returns(true);
        Service = CreateService(Database);
        User = new User("twofactor@example.com", "twofactoruser")
        {
            Local = true,
            PasswordHash = Passwords.CreateSaltedPasswordHash("TestPassword123", [55, 12, 55, 50]),
        };
        User.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        Database.Users.Add(User);
        Session = new Session { User = User, LastLoggedIn = Clock.GetUtcNow().UtcDateTime };
        Database.Sessions.Add(Session);
        Database.SaveChanges();
    }

    public NotificationsEnabledDb Database { get; }

    public IConfigurationRoot Configuration { get; }

    public TestClock Clock { get; } = new();

    public IMailQueue MailQueue { get; } = Substitute.For<IMailQueue>();

    public IMailSender MailSender { get; } = Substitute.For<IMailSender>();

    public IBackgroundJobClient Jobs { get; } = Substitute.For<IBackgroundJobClient>();

    public TwoFactorService Service { get; }

    public User User { get; }

    public Session Session { get; }

    public TwoFactorAuthentication Credential => User.TwoFactorAuthentication!;

    public string Secret { get; private set; } = string.Empty;

    public List<string> BackupCodes { get; private set; } = new();

    public string CurrentCode => new Totp(Base32Encoding.ToBytes(Secret)).ComputeTotp(Clock.GetUtcNow().UtcDateTime);

    public NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(options, Substitute.For<IModelUpdateNotificationSender>());
    }

    public TwoFactorService CreateService(NotificationsEnabledDb database)
    {
        return new TwoFactorService(database, protection, Clock, MailQueue, Jobs, Configuration);
    }

    public TwoFactorRecoveryJob CreateRecoveryJob()
    {
        return new TwoFactorRecoveryJob(Database, Service, MailSender, Configuration,
            NullLogger<TwoFactorRecoveryJob>.Instance);
    }

    public async Task Enable()
    {
        var setup = await Service.CreateSetup(User, Session);
        Secret = setup.Secret;
        BackupCodes = (await Service.Enable(User.Id, Session, CurrentCode))!;
    }

    public void Dispose()
    {
        Database.Dispose();
    }
}

public class TestClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }

    public void Advance(TimeSpan time)
    {
        now += time;
    }
}
