namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Controllers;
using Server.Jobs;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class PublishTranslationProjectJobRealDbTests : IClassFixture<RealUnitTestDatabaseFixture>
{
    private readonly RealUnitTestDatabaseFixture fixture;

    public PublishTranslationProjectJobRealDbTests(RealUnitTestDatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task EditorSaveWhilePublishIsUnlockedRemainsPendingAfterSnapshotIsPushed()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.GetConnectionStringForAdditionalDatabase()).UseSnakeCaseNamingConvention().Options;
        await using var setup = new ApplicationDbContext(options);
        var project = new TranslationProject
        {
            Slug = "concurrent-publish-" + Guid.NewGuid(), Format = TranslationFormat.Resx,
            RepositoryFullName = "example/translations", SourceBranch = "main", SourceFilePattern = "en.resx",
            TranslationFilePattern = "{language}.resx", LastImportedSha = "upstream", CurrentRevision = 1,
        };
        await setup.TranslationProjects.AddAsync(project);
        await setup.SaveChangesAsync();
        foreach (var code in new[] { "en", "fi" })
        {
            await setup.TranslationProjectLanguages.AddAsync(new TranslationProjectLanguage
            {
                Project = project, LanguageCode = code,
            });
        }

        await setup.TranslationUnits.AddAsync(new TranslationUnit
        {
            Project = project, Key = "HELLO", SourceText = "English", SourceRevision = 1,
        });
        var pendingSince = DateTime.UtcNow.AddMinutes(-10);
        foreach (var code in new[] { "en", "fi" })
        {
            await setup.TranslationValues.AddAsync(new TranslationValue
            {
                ProjectId = project.Id, Key = "HELLO", LanguageCode = code,
                Text = code == "en" ? "English" : "First edit",
                LastImportedText = code == "en" ? "English" : "Upstream",
                State = TranslationState.Translated, PendingRevision = code == "fi" ? 1 : null,
                PendingSince = code == "fi" ? pendingSince : null,
            });
        }

        await setup.SaveChangesAsync();
        var checkout = Directory.CreateTempSubdirectory("translation-concurrent-publish-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "en.resx"),
                "<root><data name=\"HELLO\"><value>English</value></data></root>");
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "fi.resx"),
                "<root><data name=\"HELLO\"><value>Upstream</value></data></root>");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var unlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var locks = Substitute.For<ILocalTempFileLocks>();
            locks.GetTempFilePath($"translationRepos/{project.Id}").Returns(checkout.FullName);
            locks.LockAsync(checkout.FullName, Arg.Any<CancellationToken>())
                .Returns(new ValueTask<IDisposable>(Substitute.For<IDisposable>()));
            var repository = Substitute.For<TranslationRepositoryOperations>();
            repository.PrepareCheckout(project.RepositoryFullName, "main", checkout.FullName,
                Arg.Any<CancellationToken>()).Returns("upstream");
            repository.CheckoutNewBranch(checkout.FullName, Arg.Any<string>(), "main",
                Arg.Any<CancellationToken>()).Returns(async call =>
            {
                unlocked.SetResult();
                await resume.Task.WaitAsync(call.Arg<CancellationToken>());
            });
            repository.GetCurrentCommit(checkout.FullName, Arg.Any<CancellationToken>()).Returns("published-head");
            var github = Substitute.For<ITranslationGithubClient>();
            repository.CreateGithubClient("token").Returns(github);
            github.GetLogin(Arg.Any<CancellationToken>()).Returns("bot");
            github.CreatePullRequest(project.RepositoryFullName, "main", "bot", Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new TranslationGithubPullRequest(42, true,
                "published-head", "unused", "bot", "main", true));
            var tokens = Substitute.For<ITranslationGithubTokenProvider>();
            tokens.GetToken(Arg.Any<CancellationToken>()).Returns("token");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BaseUrl"] = "https://example.com/", ["Email:FromAddress"] = "translations@example.com",
            }).Build();
            await using var worker = new ApplicationDbContext(options);
            var job = new PublishTranslationProjectJob(worker, locks, tokens, configuration,
                NullLogger<PublishTranslationProjectJob>.Instance, Substitute.For<IModelUpdateNotificationSender>(),
                repository);
            var publishing = job.Execute(project.Id, timeout.Token);
            try
            {
                await unlocked.Task.WaitAsync(timeout.Token);
                await using var editorDatabase = new NotificationsEnabledDb(options,
                    Substitute.For<IModelUpdateNotificationSender>());
                Assert.False(await TranslationProjectAdvisoryLock.IsHeldAsync(editorDatabase, project.Id,
                    timeout.Token));
                var actor = await editorDatabase.Users.FirstAsync(timeout.Token);
                actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User));
                var context = new DefaultHttpContext { RequestAborted = timeout.Token };
                context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
                var editor = new TranslationEditorController(NullLogger<TranslationEditorController>.Instance,
                    editorDatabase)
                {
                    ControllerContext = new ControllerContext { HttpContext = context },
                };
                var current = await editorDatabase.TranslationValues.SingleAsync(value =>
                    value.ProjectId == project.Id && value.LanguageCode == "fi", timeout.Token);
                var saved = await editor.Save(new TranslationSaveRequest
                {
                    ProjectId = project.Id, Key = "HELLO", LanguageCode = "fi", Text = "Latest edit",
                    State = TranslationState.Translated, Version = current.Version,
                }).WaitAsync(timeout.Token);
                Assert.NotNull(saved.Value);
                Assert.Equal("Latest edit", saved.Value.Text);
                Assert.Equal(2, current.PendingRevision);
            }
            finally
            {
                resume.TrySetResult();
                await publishing.WaitAsync(timeout.Token);
            }

            await using var verification = new ApplicationDbContext(options);
            var latest = await verification.TranslationValues.SingleAsync(value =>
                value.ProjectId == project.Id && value.LanguageCode == "fi");
            Assert.Equal("Latest edit", latest.Text);
            Assert.Equal(2, latest.PendingRevision);
            Assert.NotNull(latest.PendingSince);
            Assert.Equal(1, latest.LastPublishedRevision);
            Assert.Equal(1, (await verification.TranslationPullRequests.SingleAsync(pr =>
                pr.ProjectId == project.Id)).LastPublishedRevision);
            var published = await File.ReadAllTextAsync(Path.Combine(checkout.FullName, "fi.resx"));
            Assert.Contains("First edit", published);
            Assert.DoesNotContain("Latest edit", published);
            await repository.Received(1).PushBranch(checkout.FullName,
                "https://github.com/bot/translations.git", Arg.Any<string>(), string.Empty,
                Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            checkout.Delete(true);
            await setup.TranslationChanges.IgnoreQueryFilters().Where(change => change.ProjectId == project.Id)
                .ExecuteDeleteAsync();
            await setup.TranslationPullRequests.IgnoreQueryFilters().Where(pr => pr.ProjectId == project.Id)
                .ExecuteDeleteAsync();
            await setup.TranslationSyncRuns.IgnoreQueryFilters().Where(run => run.ProjectId == project.Id)
                .ExecuteDeleteAsync();
            await setup.TranslationValues.IgnoreQueryFilters().Where(value => value.ProjectId == project.Id)
                .ExecuteDeleteAsync();
            await setup.TranslationUnits.IgnoreQueryFilters().Where(unit => unit.ProjectId == project.Id)
                .ExecuteDeleteAsync();
            await setup.TranslationProjectLanguages.IgnoreQueryFilters().Where(language =>
                language.ProjectId == project.Id).ExecuteDeleteAsync();
            await setup.TranslationProjects.Where(item => item.Id == project.Id).ExecuteDeleteAsync();
        }
    }
}
