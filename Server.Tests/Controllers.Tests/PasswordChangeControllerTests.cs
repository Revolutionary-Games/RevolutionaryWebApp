namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Security.Principal;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Authorization;
using Server.Controllers;
using Shared;
using Shared.Forms;
using Shared.Models;
using Xunit;

public sealed class PasswordChangeControllerTests : IDisposable
{
    private readonly TwoFactorTestContext test = new();
    private readonly DefaultHttpContext context = new();
    private readonly PasswordChangeController controller;

    public PasswordChangeControllerTests()
    {
        context.User = new GenericPrincipal(new GenericIdentity(test.User.UserName), null);
        context.Items[AppInfo.CurrentUserMiddlewareKey] = test.User;
        context.Items[AppInfo.CurrentUserSessionMiddleWareKey] = test.Session;
        controller = new PasswordChangeController(test.Database, test.Service,
            NullLogger<PasswordChangeController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    [Fact]
    public async Task ChangeRequiresCurrentPassword()
    {
        var status = (await controller.GetStatus()).Value!;
        Assert.True(status.HasPassword);
        Assert.False(status.TwoFactorEnabled);

        var request = new ChangePasswordRequest { CurrentPassword = "wrong", NewPassword = "NewPassword456" };
        Assert.IsType<BadRequestObjectResult>(await controller.ChangePassword(request));

        request.CurrentPassword = "TestPassword123";
        Assert.IsType<OkResult>(await controller.ChangePassword(request));

        await using var database = test.CreateDatabase();
        var updated = await database.Users.SingleAsync();
        Assert.True(Passwords.CheckPassword(updated.PasswordHash!, request.NewPassword));
        Assert.False(Passwords.CheckPassword(updated.PasswordHash!, request.CurrentPassword));
        Assert.Equal(UserNotificationType.PasswordChanged, (await database.UserNotifications.SingleAsync()).Type);
    }

    [Fact]
    public async Task SsoAccountWithoutPasswordMustUseEmail()
    {
        test.User.Local = false;
        test.User.SsoSource = LoginController.SsoTypeGithub;
        test.User.PasswordHash = null;
        await test.Database.SaveChangesAsync();

        Assert.False((await controller.GetStatus()).Value!.HasPassword);
        var result = Assert.IsType<BadRequestObjectResult>(await controller.ChangePassword(new ChangePasswordRequest
        {
            CurrentPassword = "TestPassword123",
            NewPassword = "NewPassword456",
        }));
        Assert.Contains("Forgot password", Assert.IsType<string>(result.Value));
        Assert.Null(test.User.PasswordHash);
        Assert.False(test.User.Local);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangeRequiresFreshCodeWhenTwoFactorIsEnabled(bool useBackupCode)
    {
        await test.Enable();
        test.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True((await controller.GetStatus()).Value!.TwoFactorEnabled);
        var request = new ChangePasswordRequest
        {
            CurrentPassword = "TestPassword123",
            NewPassword = "NewPassword456",
        };

        Assert.IsType<BadRequestObjectResult>(await controller.ChangePassword(request));
        request.TwoFactorCode = "invalid";
        Assert.IsType<BadRequestObjectResult>(await controller.ChangePassword(request));
        Assert.Equal(1, test.Credential.FailedAttempts);
        Assert.True(Passwords.CheckPassword(test.User.PasswordHash!, request.CurrentPassword));

        request.TwoFactorCode = useBackupCode ? test.BackupCodes[0] : test.CurrentCode;
        Assert.IsType<OkResult>(await controller.ChangePassword(request));
        Assert.True(Passwords.CheckPassword(test.User.PasswordHash!, request.NewPassword));

        request.CurrentPassword = request.NewPassword;
        request.NewPassword = "AnotherPassword789";
        Assert.IsType<BadRequestObjectResult>(await controller.ChangePassword(request));
        Assert.True(Passwords.CheckPassword(test.User.PasswordHash!, request.CurrentPassword));
    }

    [Fact]
    public async Task ChangeRequiresBrowserSession()
    {
        context.Items.Remove(AppInfo.CurrentUserSessionMiddleWareKey);

        Assert.IsType<UnauthorizedResult>((await controller.GetStatus()).Result);
        Assert.IsType<UnauthorizedResult>(await controller.ChangePassword(new ChangePasswordRequest
        {
            CurrentPassword = "TestPassword123",
            NewPassword = "NewPassword456",
        }));
    }

    public void Dispose()
    {
        controller.Dispose();
        test.Dispose();
    }
}
