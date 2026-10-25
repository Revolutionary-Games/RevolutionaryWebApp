namespace RevolutionaryWebApp.Server.Tests.Authorization.Tests;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Authorization;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class TranslatorAgreementTests
{
    [Theory]
    [InlineData(false, 403)]
    [InlineData(true, null)]
    public async Task AgreementIsRequiredEvenForAdmins(bool accepted, int? expectedStatus)
    {
        var user = new User("translator@example.com", "Translator") { AcceptedTranslator = accepted };
        user.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.Admin));
        var http = CreateContext(user);
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());

        await new RequireTranslatorAgreementAttribute().OnAuthorizationAsync(context);

        if (expectedStatus == null)
        {
            Assert.Null(context.Result);
        }
        else
        {
            Assert.Equal(expectedStatus, Assert.IsType<ObjectResult>(context.Result).StatusCode);
        }
    }

    [Theory]
    [InlineData(typeof(TranslationEditorController), "Save", true)]
    [InlineData(typeof(ManualTranslationKeysController), "Create", true)]
    [InlineData(typeof(TranslationSuggestionsController), "Create", true)]
    [InlineData(typeof(TranslationSuggestionsController), "Accept", true)]
    [InlineData(typeof(TranslationSuggestionsController), "Vote", false)]
    [InlineData(typeof(TranslationDiscussionController), "AddComment", false)]
    [InlineData(typeof(TranslationDictionariesController), "AddTerm", true)]
    [InlineData(typeof(TranslationDictionariesController), "AddLanguageTerm", true)]
    [InlineData(typeof(TranslationDictionariesController), "SaveSourceTerm", true)]
    [InlineData(typeof(TranslationDictionariesController), "SaveLanguageTerm", true)]
    [InlineData(typeof(TranslationDictionariesController), "SaveTranslation", true)]
    [InlineData(typeof(TranslationDictionariesController), "RequestTerm", true)]
    [InlineData(typeof(TranslationDictionariesController), "ApproveRequest", true)]
    public void ContributionEndpointsRequireAgreement(Type controller, string method, bool required)
    {
        var action = controller.GetMethod(method);
        Assert.NotNull(action);
        Assert.Equal(required, action.GetCustomAttribute<RequireTranslatorAgreementAttribute>() != null);
    }

    [Fact]
    public async Task AgreePersistsAcceptanceAndCanBeRepeated()
    {
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IModelUpdateNotificationSender>());
        var user = new User("translator@example.com", "Translator");
        Assert.False(user.AcceptedTranslator);
        user.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User));
        database.Users.Add(user);
        await database.SaveChangesAsync();
        var controller = new TranslationAgreementController(database)
        {
            ControllerContext = new ControllerContext { HttpContext = CreateContext(user) },
        };

        var result = await controller.Agree();
        Assert.True(result.Value!.AcceptedTranslator);
        database.ChangeTracker.Clear();
        var reloadedUser = (await database.Users.FindAsync(user.Id))!;
        Assert.True(reloadedUser.AcceptedTranslator);
        reloadedUser.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        Assert.True((await controller.Agree()).Value!.AcceptedTranslator);
    }

    private static DefaultHttpContext CreateContext(User user)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity("Test")),
        };
        context.Items[AppInfo.CurrentUserMiddlewareKey] = user;
        return context;
    }
}
