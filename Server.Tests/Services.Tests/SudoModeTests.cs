namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Server.Models;
using Server.Services;
using Xunit;

public sealed class SudoModeTests : IDisposable
{
    private readonly TwoFactorTestContext test = new();

    private readonly ServiceProvider services =
        new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();

    private readonly SudoModeService sudo;

    public SudoModeTests()
    {
        sudo = new SudoModeService(test.Database, test.Service, services.GetRequiredService<IDistributedCache>());
    }

    [Fact]
    public async Task RequiresEnabledTwoFactorAndFreshCodeEvenAfterLoginOrSetup()
    {
        Assert.False(await sudo.Enter(test.Session, test.User.Id, "123456"));
        Assert.False(await sudo.IsActive(test.Session, test.User.Id));
        await test.Enable();
        Assert.NotNull(test.Session.TwoFactorVerifiedAt);
        Assert.False(await sudo.IsActive(test.Session, test.User.Id));
        Assert.False(await sudo.Enter(test.Session, test.User.Id, test.CurrentCode));

        test.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(await sudo.Enter(test.Session, test.User.Id, test.CurrentCode));
        Assert.True(await sudo.IsActive(test.Session, test.User.Id));
        Assert.False(await sudo.Enter(test.Session, test.User.Id, test.CurrentCode));
    }

    [Fact]
    public async Task ExpiresAfterExactlyFifteenMinutesWithoutSlidingOnUse()
    {
        await test.Enable();
        Assert.True(await sudo.Enter(test.Session, test.User.Id, test.BackupCodes[0]));
        test.Clock.Advance(TimeSpan.FromMinutes(14));
        Assert.True(await sudo.IsActive(test.Session, test.User.Id));
        test.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(await sudo.IsActive(test.Session, test.User.Id));

        Assert.True(await sudo.Enter(test.Session, test.User.Id, test.BackupCodes[1]));
        Assert.True(await sudo.IsActive(test.Session, test.User.Id));
    }

    [Fact]
    public async Task GrantIsLimitedToSessionUserAndCurrentAuthenticator()
    {
        await test.Enable();
        Assert.True(await sudo.Enter(test.Session, test.User.Id, test.BackupCodes[0]));
        var otherSession = new Session { UserId = test.User.Id };
        Assert.False(await sudo.IsActive(otherSession, test.User.Id));
        Assert.False(await sudo.IsActive(test.Session, test.User.Id + 1));
        Assert.False(await sudo.Enter(test.Session, test.User.Id + 1, test.BackupCodes[1]));
        Assert.Equal(9, TwoFactorService.GetBackupCodeHashes(test.Credential).Count);

        test.Credential.KeyVersion = Guid.NewGuid();
        await test.Service.SaveCredential(test.Credential);
        Assert.False(await sudo.IsActive(test.Session, test.User.Id));
        Assert.True(await sudo.Enter(test.Session, test.User.Id, test.BackupCodes[1]));

        Assert.True(await test.Service.Disable(test.User.Id, test.Session, test.BackupCodes[2]));
        Assert.False(await sudo.IsActive(test.Session, test.User.Id));
        Assert.False(await sudo.Enter(test.Session, test.User.Id, test.BackupCodes[3]));
    }

    [Fact]
    public async Task UsesAccountWideLockoutAndSingleUseBackupCodes()
    {
        await test.Enable();
        for (int i = 0; i < 5; ++i)
            Assert.False(await sudo.Enter(test.Session, test.User.Id, "invalid"));

        Assert.False(await sudo.Enter(test.Session, test.User.Id, test.BackupCodes[0]));
        Assert.False(await sudo.IsActive(test.Session, test.User.Id));
        Assert.Equal(10, TwoFactorService.GetBackupCodeHashes(test.Credential).Count);
        test.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(await sudo.Enter(test.Session, test.User.Id, test.BackupCodes[0]));
        Assert.False(await sudo.Enter(test.Session, test.User.Id, test.BackupCodes[0]));
        Assert.Equal(9, TwoFactorService.GetBackupCodeHashes(test.Credential).Count);
    }

    [Fact]
    public async Task PendingLoginCannotEnterSudoMode()
    {
        await test.Enable();
        test.Session.PendingTwoFactorUserId = test.User.Id;
        Assert.False(await sudo.Enter(test.Session, test.User.Id, test.BackupCodes[0]));
        Assert.False(await sudo.IsActive(test.Session, test.User.Id));
        Assert.Equal(10, TwoFactorService.GetBackupCodeHashes(test.Credential).Count);
    }

    public void Dispose()
    {
        services.Dispose();
        test.Dispose();
    }
}
