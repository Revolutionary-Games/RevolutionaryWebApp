namespace RevolutionaryWebApp.Client.Tests.Pages.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Blazored.Modal;
using Blazored.Modal.Services;
using Bunit;
using Client.Pages;
using Client.Shared;
using Client.Shared.Modals;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Mocks;
using NSubstitute;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Models.Enums;
using RichardSzalay.MockHttp;
using Services;
using Xunit;

public sealed class TranslationEditorTests : BunitContext, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("text", false)]
    [InlineData("text", true)]
    [InlineData("state", false)]
    [InlineData("explanation", false)]
    public async Task UnsavedChangesRequireConfirmation(string field, bool leave)
    {
        var modal = Substitute.For<IModalService>();
        var reference = Substitute.For<IModalReference>();
        reference.Result.Returns(Task.FromResult(leave ? ModalResult.Ok(true) : ModalResult.Cancel()));
        modal.Show<ConfirmAction>(Arg.Any<string>(), Arg.Any<ModalParameters>()).Returns(reference);
        var page = PrepareEditor(modal);
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        Assert.False(page.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);

        switch (field)
        {
            case "text":
                await page.InvokeAsync(() => page.Find("#translation-text").Input("Edited source"));
                break;
            case "state":
                await page.InvokeAsync(() => page.FindAll("input[type=radio]").Last()
                    .Change(nameof(TranslationState.Verified)));
                break;
            case "explanation":
                await page.InvokeAsync(() => page.Find("#source-explanation").Input("More context"));
                break;
        }

        await page.WaitForAssertionAsync(() =>
            Assert.True(page.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation));
        await page.InvokeAsync(() => navigation.NavigateTo("translations/thrive/en"));

        modal.Received(1).Show<ConfirmAction>("Unsaved translation changes", Arg.Any<ModalParameters>());
        Assert.Equal(leave ? "http://localhost/translations/thrive/en" : originalUri, navigation.Uri);
        if (!leave && field == "text")
            Assert.Equal("Edited source", page.Find("#translation-text").GetAttribute("value"));
    }

    [Fact]
    public async Task RevertingChangesAllowsNavigationWithoutConfirmation()
    {
        var modal = Substitute.For<IModalService>();
        var page = PrepareEditor(modal);
        await page.InvokeAsync(() => page.Find("#source-explanation").Input("Changed"));
        await page.WaitForAssertionAsync(() =>
            Assert.True(page.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation));
        await page.InvokeAsync(() => page.Find("#source-explanation").Input("Context"));

        await page.WaitForAssertionAsync(() =>
            Assert.False(page.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation));
        var navigation = Services.GetRequiredService<NavigationManager>();
        await page.InvokeAsync(() => navigation.NavigateTo("translations/thrive/en"));

        Assert.Equal("http://localhost/translations/thrive/en", navigation.Uri);
        modal.DidNotReceive().Show<ConfirmAction>(Arg.Any<string>(), Arg.Any<ModalParameters>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlySuccessfulSaveClearsWarning(bool success)
    {
        var page = PrepareEditor(Substitute.For<IModalService>(), success);
        await page.InvokeAsync(() => page.Find("#source-explanation").Input("Changed"));
        await page.InvokeAsync(() => page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Save").Click());

        await page.WaitForAssertionAsync(() =>
        {
            Assert.Contains(success ? "Translation saved." : "Save failed:", page.Markup);
            Assert.Equal(!success, page.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);
        });
    }

    [Fact]
    public async Task DraftOnAnotherKeyStillRequiresConfirmation()
    {
        var modal = Substitute.For<IModalService>();
        var reference = Substitute.For<IModalReference>();
        reference.Result.Returns(Task.FromResult(ModalResult.Ok(true)), Task.FromResult(ModalResult.Cancel()));
        modal.Show<ConfirmAction>(Arg.Any<string>(), Arg.Any<ModalParameters>()).Returns(reference);
        var page = PrepareEditor(modal);
        await page.InvokeAsync(() => page.Find("#translation-text").Input("Unsaved draft"));
        await page.InvokeAsync(() => page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Next →").Click());

        await page.WaitForAssertionAsync(() => Assert.Equal("NEXT", page.Instance.Key));
        await page.WaitForAssertionAsync(() =>
            Assert.Equal("Next source", page.Find("#translation-text").GetAttribute("value")));
        Assert.True(page.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        await page.InvokeAsync(() => navigation.NavigateTo("translations/thrive/en"));

        modal.Received(2).Show<ConfirmAction>("Unsaved translation changes", Arg.Any<ModalParameters>());
        Assert.Equal(originalUri, navigation.Uri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAndContinueStopsForActiveWarningsAndAllowsIgnoredWarnings(bool ignored)
    {
        var page = PrepareEditor(Substitute.For<IModalService>(), savedProblems:
        [
            new TranslationProblemDTO { Id = 1, Message = "Fix capitalization", Ignored = ignored },
        ]);
        await page.InvokeAsync(() => page.Find("#source-explanation").Input("Changed"));
        await page.InvokeAsync(() => page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Save & Continue").Click());

        await page.WaitForAssertionAsync(() =>
        {
            if (ignored)
            {
                Assert.Equal("NEXT", page.Instance.Key);
            }
            else
            {
                Assert.Equal("KEY", page.Instance.Key);
                Assert.Contains("Please review the warnings before continuing", page.Markup);
                Assert.Contains("Fix capitalization", page.Markup);
                Assert.Contains("Ignore warning", page.Markup);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IgnoringWarningRequiresConfirmationAndMovesItBelowEditor(bool confirm)
    {
        var modal = Substitute.For<IModalService>();
        var reference = Substitute.For<IModalReference>();
        reference.Result.Returns(Task.FromResult(confirm ? ModalResult.Ok(true) : ModalResult.Cancel()));
        modal.Show<ConfirmAction>(Arg.Any<string>(), Arg.Any<ModalParameters>()).Returns(reference);
        var page = PrepareEditor(modal, initialProblems:
        [
            new TranslationProblemDTO { Id = 1, Message = "Fix capitalization" },
        ]);
        await page.InvokeAsync(() => page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Ignore warning").Click());

        modal.Received(1).Show<ConfirmAction>("Ignore translation warning?", Arg.Any<ModalParameters>());
        await page.WaitForAssertionAsync(() =>
        {
            Assert.Equal(!confirm, page.FindAll("button").Any(button => button.TextContent.Trim() == "Ignore warning"));
            Assert.Equal(confirm, page.Markup.Contains("Ignored warning: Fix capitalization",
                StringComparison.Ordinal));
            if (confirm)
            {
                Assert.Contains("Ignored by", page.Markup);
                Assert.Equal(1, page.FindComponent<UsernameDisplayer>().Instance.UserId);
                Assert.Contains(page.FindComponents<RecentTimeShortener>(), component =>
                    component.Instance.Time == new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc));
                var editorPosition = page.Markup.IndexOf("translation-text", StringComparison.Ordinal);
                var warningPosition = page.Markup.IndexOf("Ignored warning: Fix capitalization",
                    StringComparison.Ordinal);
                Assert.True(warningPosition > editorPosition);
            }
        });
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        return DisposeAsync().AsTask();
    }

    private IRenderedComponent<TranslationEditor> PrepareEditor(IModalService modal, bool saveSucceeds = true,
        TranslationProblemDTO[]? savedProblems = null, TranslationProblemDTO[]? initialProblems = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var userInfo = new CurrentUserInfo();
        userInfo.OnReceivedOurInfo(new UserDTO
        {
            Id = 1, Groups = new CachedUserGroups(GroupType.User), AcceptedTranslator = true,
        });
        Services.AddSingleton(userInfo);
        Services.AddSingleton(CSRFMockFactory.Create());
        Services.AddSingleton<NotificationHandler>();
        Services.AddSingleton<UsernameRetriever>();
        ComponentFactories.AddStub<TranslationAnnouncementBanner>();
        var http = Services.AddMockHttpClient();
        http.When(HttpMethod.Post, "/api/v1/UserManagement/usernames")
            .RespondJson(new Dictionary<long, string> { { 1, "Translator" } });
        http.When(HttpMethod.Get, "/api/v1/translations/projects/thrive")
            .RespondJson(new TranslationProjectDTO { Id = 1, Slug = "thrive", SourceLanguage = "en" });
        var data = new TranslationEditorDTO
        {
            ProjectId = 1, LanguageCode = "en", Key = "KEY", Text = "Source", Explanation = "Context",
            CanEditSource = true, CanVerify = true, State = TranslationState.Translated,
            Problems = initialProblems ?? [],
        };
        http.When(HttpMethod.Get, "/api/v1/translationEditor").WithQueryString("key", "NEXT")
            .RespondJson(new TranslationEditorDTO
            {
                ProjectId = 1, LanguageCode = "en", Key = "NEXT", Text = "Next source",
                CanEditSource = true, State = TranslationState.Translated,
            });
        http.When(HttpMethod.Get, "/api/v1/translationEditor").WithQueryString("key", "KEY").RespondJson(data);
        http.When(HttpMethod.Get, "/api/v1/translationEditor/neighbor").RespondJson("NEXT");
        http.When(HttpMethod.Post, "/api/v1/translationProblems/1/ignore").Respond(HttpStatusCode.NoContent);
        http.When(HttpMethod.Get, "/api/v1/translationProblems").RespondJson(new[]
        {
            new TranslationProblemDTO
            {
                Id = 1, Message = "Fix capitalization", Ignored = true, IgnoredByUserId = 1,
                IgnoredAt = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc),
            },
        });
        http.When(HttpMethod.Get, "/api/v1/translationDiscussion/comments")
            .RespondJson(Array.Empty<TranslationCommentDTO>());
        http.When(HttpMethod.Get, "/api/v1/translationSuggestions")
            .RespondJson(Array.Empty<TranslationSuggestionDTO>());
        http.When(HttpMethod.Get, "/api/v1/translationDictionaries/matching")
            .RespondJson(Array.Empty<DictionaryTermDTO>());
        http.When(HttpMethod.Get, "/api/v1/translationEditor/history")
            .RespondJson(Array.Empty<TranslationHistoryDTO>());
        if (saveSucceeds)
        {
            http.When(HttpMethod.Put, "/api/v1/translationEditor").RespondJson(new TranslationEditorDTO
            {
                ProjectId = 1, LanguageCode = "en", Key = "KEY", Text = "Source", Explanation = "Changed",
                CanEditSource = true, State = TranslationState.Translated, Problems = savedProblems ?? [],
            });
            http.When(HttpMethod.Get, "/api/v1/translationSubscriptions/prompt")
                .RespondJson(new TranslationSubscriptionPromptDTO());
        }
        else
        {
            http.When(HttpMethod.Put, "/api/v1/translationEditor").Respond(HttpStatusCode.Conflict);
        }

        Services.GetRequiredService<NavigationManager>().NavigateTo("translations/thrive/en/edit?key=KEY");
        var page = Render<TranslationEditor>(parameters => parameters
            .Add(p => p.Slug, "thrive").Add(p => p.LanguageCode, "en")
            .AddCascadingValue(modal));
        page.WaitForAssertion(() => Assert.True(
            page.Find("#translation-text").GetAttribute("value") == "Source", page.Markup));
        return page;
    }
}
