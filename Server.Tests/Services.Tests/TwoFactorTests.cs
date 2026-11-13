namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using OtpNet;
using Server.Filters;
using Server.Models;
using Server.Models.Emails;
using Server.Services;
using Server.Utilities;
using Shared.Models.Enums;
using Xunit;

public class TwoFactorTests
{
    [Fact]
    public async Task SetupProtectsSecretAndRequiresConfirmationBeforeEnabling()
    {
        using var test = new TwoFactorTestContext();
        var setup = await test.Service.CreateSetup(test.User, test.Session);

        Assert.False(test.Credential.Enabled);
        Assert.DoesNotContain(setup.Secret, test.Credential.ProtectedSecret!);
        Assert.StartsWith("data:image/png;base64,iVBOR", setup.QrCodeDataUrl);
        Assert.Null(await test.Service.Enable(test.User.Id, test.Session, "wrong"));
        Assert.False(test.Credential.Enabled);
        var code = new Totp(Base32Encoding.ToBytes(setup.Secret)).ComputeTotp(test.Clock.GetUtcNow().UtcDateTime);
        var codes = await test.Service.Enable(test.User.Id, test.Session, code);

        Assert.True(test.Credential.Enabled);
        Assert.NotNull(codes);
        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, c => Assert.DoesNotContain(c.Replace("-", string.Empty), test.Credential.BackupCodeHashes));
        Assert.NotNull(test.Session.TwoFactorVerifiedAt);
        Assert.Null(test.Credential.SetupExpiresAt);
    }

    [Fact]
    public async Task SetupExpiresAndRequiresRecentBrowserLogin()
    {
        using var test = new TwoFactorTestContext();
        var setup = await test.Service.CreateSetup(test.User, test.Session);
        test.Clock.Advance(TimeSpan.FromMinutes(21));
        var code = new Totp(Base32Encoding.ToBytes(setup.Secret)).ComputeTotp(test.Clock.GetUtcNow().UtcDateTime);

        await Assert.ThrowsAsync<HttpResponseException>(() => test.Service.Enable(test.User.Id, test.Session, code));
        await test.Service.CreateSetup(test.User, test.Session);
        test.Clock.Advance(TimeSpan.FromMinutes(9));
        await test.Service.CreateSetup(test.User, test.Session);
        test.Clock.Advance(TimeSpan.FromTicks(1));
        await Assert.ThrowsAsync<HttpResponseException>(() => test.Service.CreateSetup(test.User, test.Session));
        Assert.False(test.Credential.Enabled);
    }

    [Fact]
    public async Task EnablingRevokesExistingSessionsAndPersistsVerificationWithSeparateContexts()
    {
        using var test = new TwoFactorTestContext();
        var other = new Session { UserId = test.User.Id };
        test.Database.Sessions.Add(other);
        await test.Database.SaveChangesAsync();
        await using var authenticationDatabase = test.CreateDatabase();
        var authenticationSession = await authenticationDatabase.Sessions.SingleAsync(s => s.Id == test.Session.Id);
        var setup = await test.Service.CreateSetup(test.User, authenticationSession);
        var code = new Totp(Base32Encoding.ToBytes(setup.Secret)).ComputeTotp(test.Clock.GetUtcNow().UtcDateTime);

        await test.Service.Enable(test.User.Id, authenticationSession, code);

        Assert.Equal(test.Session.Id, (await test.Database.Sessions.SingleAsync()).Id);
        Assert.NotNull((await test.Database.Sessions.AsNoTracking().SingleAsync()).TwoFactorVerifiedAt);
    }

    [Fact]
    public async Task AuthenticatorCodesAllowClockSkewButCannotBeReplayed()
    {
        using var test = new TwoFactorTestContext();
        await test.Enable();

        Assert.False(await test.Service.VerifyCode(test.Credential, test.CurrentCode));
        test.Clock.Advance(TimeSpan.FromSeconds(60));
        var previous = new Totp(Base32Encoding.ToBytes(test.Secret))
            .ComputeTotp(test.Clock.GetUtcNow().UtcDateTime.AddSeconds(-30));
        Assert.True(await test.Service.VerifyCode(test.Credential, previous));
        Assert.False(await test.Service.VerifyCode(test.Credential, previous));
        Assert.True(await test.Service.VerifyCode(test.Credential, test.CurrentCode));
    }

    [Fact]
    public async Task FailedAttemptsLockAccountAndSetupCannotResetLockout()
    {
        using var test = new TwoFactorTestContext();
        await test.Enable();
        for (int i = 0; i < 5; ++i)
            Assert.False(await test.Service.VerifyCode(test.Credential, "wrong"));

        Assert.False(await test.Service.VerifyCode(test.Credential, test.BackupCodes[0]));
        await Assert.ThrowsAsync<HttpResponseException>(() => test.Service.CreateSetup(test.User, test.Session));
        Assert.Equal(10, TwoFactorService.GetBackupCodeHashes(test.Credential).Count);
        test.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.True(await test.Service.VerifyCode(test.Credential, test.BackupCodes[0]));
        Assert.Null(test.Credential.LockedUntil);
    }

    [Fact]
    public async Task BackupCodesAreNormalizedSingleUseAndReplacementInvalidatesOldCodes()
    {
        using var test = new TwoFactorTestContext();
        await test.Enable();

        Assert.True(await test.Service.VerifyCode(test.Credential, test.BackupCodes[0].ToLowerInvariant()));
        Assert.False(await test.Service.VerifyCode(test.Credential, test.BackupCodes[0]));
        Assert.Equal(9, (await test.Service.GetStatus(test.User.Id)).BackupCodesRemaining);
        var replacements = await test.Service.RegenerateBackupCodes(test.User.Id, test.BackupCodes[1]);
        Assert.Equal(10, replacements!.Count);
        Assert.False(await test.Service.VerifyCode(test.Credential, test.BackupCodes[2]));
        Assert.True(await test.Service.VerifyCode(test.Credential, replacements[0]));
    }

    [Fact]
    public async Task ConcurrentRequestsCannotConsumeSameBackupCodeTwice()
    {
        using var test = new TwoFactorTestContext();
        await test.Enable();
        await using var otherDatabase = test.CreateDatabase();
        var otherCredential = await otherDatabase.TwoFactorAuthentications.SingleAsync();
        var otherService = test.CreateService(otherDatabase);

        Assert.True(await test.Service.VerifyCode(test.Credential, test.BackupCodes[0]));
        await Assert.ThrowsAsync<HttpResponseException>(() =>
            otherService.VerifyCode(otherCredential, test.BackupCodes[0]));
        Assert.Equal(9, (await test.Service.GetStatus(test.User.Id)).BackupCodesRemaining);
    }

    [Fact]
    public async Task DisablingRequiresChallengeAndRemovesSecretsWhilePreservingSessions()
    {
        using var test = new TwoFactorTestContext();
        await test.Enable();
        test.Database.Sessions.Add(new Session { UserId = test.User.Id });
        test.Database.Sessions.Add(new Session { PendingTwoFactorUserId = test.User.Id });
        await test.Database.SaveChangesAsync();
        var sessionIds = await test.Database.Sessions.OrderBy(s => s.Id).Select(s => s.Id).ToArrayAsync();

        Assert.False(await test.Service.Disable(test.User.Id, test.Session, "wrong"));
        Assert.True(test.Credential.Enabled);
        Assert.True(await test.Service.Disable(test.User.Id, test.Session, test.BackupCodes[0]));
        Assert.False(test.Credential.Enabled);
        Assert.Null(test.Credential.ProtectedSecret);
        Assert.Equal("[]", test.Credential.BackupCodeHashes);
        Assert.Equal(sessionIds, await test.Database.Sessions.OrderBy(s => s.Id).Select(s => s.Id).ToArrayAsync());
    }

    [Fact]
    public async Task TwoFactorTimestampsKeepUtcKindForPostgreSql()
    {
        using var test = new TwoFactorTestContext();
        await test.Service.CreateSetup(test.User, test.Session);
        Assert.Equal(test.Clock.GetUtcNow().UtcDateTime, test.Service.UtcNow);
        Assert.Equal(DateTimeKind.Utc, test.Service.UtcNow.Kind);
        Assert.Equal(DateTimeKind.Utc, test.Credential.SetupExpiresAt!.Value.Kind);

        await test.Enable();
        Assert.Equal(DateTimeKind.Utc, test.Session.TwoFactorVerifiedAt!.Value.Kind);

        var availableAt = await test.Service.RequestRecovery(test.User.Id);
        Assert.Equal(DateTimeKind.Utc, availableAt.Kind);
        Assert.Equal(DateTimeKind.Utc, test.Credential.RecoveryRequestedAt!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, (await test.Service.GetStatus(test.User.Id)).RecoveryAvailableAt!.Value.Kind);

        await test.CreateRecoveryJob().Execute(CancellationToken.None);
        Assert.Equal(DateTimeKind.Utc, test.Credential.LastRecoveryWarningAt!.Value.Kind);

        for (int i = 0; i < 5; ++i)
            Assert.False(await test.Service.VerifyCode(test.Credential, "wrong"));

        Assert.Equal(DateTimeKind.Utc, test.Credential.LockedUntil!.Value.Kind);
    }

    [Fact]
    public async Task RecoverySendsThreeDailyWarningsAndNeverClearsBefore72Hours()
    {
        using var test = new TwoFactorTestContext();
        await test.Enable();
        var available = await test.Service.RequestRecovery(test.User.Id);
        var job = test.CreateRecoveryJob();
        for (int day = 0; day < 3; ++day)
        {
            await job.Execute(CancellationToken.None);
            await job.Execute(CancellationToken.None);
            Assert.True(test.Credential.Enabled);
            Assert.Equal(day + 1, test.Credential.RecoveryWarningsSent);
            test.Clock.Advance(TimeSpan.FromHours(day == 2 ? 23 : 24));
        }

        await job.Execute(CancellationToken.None);
        Assert.True(test.Credential.Enabled);
        Assert.Equal(available, await test.Service.RequestRecovery(test.User.Id));
        test.Clock.Advance(TimeSpan.FromHours(1));
        await job.Execute(CancellationToken.None);

        Assert.False(test.Credential.Enabled);
        Assert.Null(test.Credential.ProtectedSecret);
        Assert.Empty(test.Database.Sessions);
        var userId = test.User.Id;
        await test.MailSender.Received(3).SendEmail(Arg.Is<MailRequest>(m =>
            m.Category == EmailReason.AccountSecurity && m.RecipientUserId == userId &&
            m.PlainTextBody!.Contains("cancel")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoveryDoesNotClearIfWarningDeliveryFailsOrIsDelayed()
    {
        using var test = new TwoFactorTestContext();
        await test.Enable();
        await test.Service.RequestRecovery(test.User.Id);
        test.MailSender.SendEmail(Arg.Any<MailRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("SMTP unavailable")));
        test.Clock.Advance(TimeSpan.FromHours(73));
        var job = test.CreateRecoveryJob();
        await job.Execute(CancellationToken.None);

        Assert.True((await test.Database.TwoFactorAuthentications.AsNoTracking().SingleAsync()).Enabled);
        Assert.Equal(0,
            (await test.Database.TwoFactorAuthentications.AsNoTracking().SingleAsync()).RecoveryWarningsSent);
        test.MailSender.SendEmail(Arg.Any<MailRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await job.Execute(CancellationToken.None);
        Assert.Equal(1,
            (await test.Database.TwoFactorAuthentications.AsNoTracking().SingleAsync()).RecoveryWarningsSent);
        Assert.True((await test.Database.TwoFactorAuthentications.AsNoTracking().SingleAsync()).Enabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SuccessfulChallengeCancelsRecovery(bool backup)
    {
        using var test = new TwoFactorTestContext();
        await test.Enable();
        await test.Service.RequestRecovery(test.User.Id);
        var job = test.CreateRecoveryJob();
        await job.Execute(CancellationToken.None);
        test.Clock.Advance(TimeSpan.FromHours(71));

        Assert.True(await test.Service.VerifyCode(test.Credential, backup ? test.BackupCodes[0] : test.CurrentCode));
        test.Clock.Advance(TimeSpan.FromHours(2));
        await job.Execute(CancellationToken.None);

        Assert.True(test.Credential.Enabled);
        Assert.Null((await test.Service.GetStatus(test.User.Id)).RecoveryAvailableAt);
        await test.MailSender.Received(1).SendEmail(Arg.Any<MailRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestartingUnconfirmedSetupDoesNotResetAccountLockout()
    {
        using var test = new TwoFactorTestContext();
        await test.Service.CreateSetup(test.User, test.Session);
        for (int i = 0; i < 5; ++i)
            Assert.Null(await test.Service.Enable(test.User.Id, test.Session, "wrong"));

        var lockedUntil = test.Credential.LockedUntil;
        var setup = await test.Service.CreateSetup(test.User, test.Session);
        var code = new Totp(Base32Encoding.ToBytes(setup.Secret)).ComputeTotp(test.Clock.GetUtcNow().UtcDateTime);
        Assert.Null(await test.Service.Enable(test.User.Id, test.Session, code));
        Assert.Equal(lockedUntil, test.Credential.LockedUntil);
    }

    [Fact]
    public async Task ConcurrentChallengeCancellationCannotBeOverwrittenByRecoveryJob()
    {
        using var test = new TwoFactorTestContext();
        await test.Enable();
        await test.Service.RequestRecovery(test.User.Id);
        await using var otherDatabase = test.CreateDatabase();
        var credential = await otherDatabase.TwoFactorAuthentications.SingleAsync();
        var otherService = test.CreateService(otherDatabase);
        var backupCode = test.BackupCodes[0];
        test.MailSender.SendEmail(Arg.Any<MailRequest>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            Assert.True(await otherService.VerifyCode(credential, backupCode));
        });

        await test.CreateRecoveryJob().Execute(CancellationToken.None);

        var saved = await test.Database.TwoFactorAuthentications.AsNoTracking().SingleAsync();
        Assert.True(saved.Enabled);
        Assert.Null(saved.RecoveryRequestedAt);
        Assert.Equal(0, saved.RecoveryWarningsSent);
    }

    [Fact]
    public async Task SecurityWarningsCannotBeSuppressedByEmailPreferences()
    {
        using var test = new TwoFactorTestContext();
        test.Database.UserEmailPreferences.Add(new UserEmailPreferences
        {
            UserId = test.User.Id,
            DisableAllEmails = true,
        });
        await test.Database.SaveChangesAsync();

        Assert.True(await EmailHelpers.IsAllowedAsync(test.Database,
            new MailRequest(test.User.Email, "Security warning", EmailReason.AccountSecurity)
            {
                RecipientUserId = test.User.Id,
            }, CancellationToken.None));
        Assert.False(EmailReason.AccountSecurity.CanUnSubscribe());
        Assert.True(new UserEmailPreferences { DisableAllEmails = true }.Allows(EmailReason.AccountSecurity));
    }
}
