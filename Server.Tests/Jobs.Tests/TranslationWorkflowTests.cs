namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
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

public class TranslationWorkflowTests
{
    [Fact]
    public async Task FirstImportUserEditsAndNonconflictingUpstreamChangePublishUpdatedGettextCatalogs()
    {
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IModelUpdateNotificationSender>());
        var project = new TranslationProject
        {
            Name = "First deployment",
            Slug = "first-deployment",
            Format = TranslationFormat.Gettext,
            RepositoryFullName = "example/translations",
            SourceBranch = "main",
            SourceFilePattern = "template.pot",
            TranslationFilePattern = "{language}.po",
        };
        var user = new User("translator@example.com", "Translator");
        user.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User, GroupType.SourceTextTranslator));
        database.TranslationProjects.Add(project);
        database.Users.Add(user);
        await database.SaveChangesAsync();

        var checkout = Directory.CreateTempSubdirectory("translation-workflow-test-");
        try
        {
            var template = Catalog(string.Empty, string.Empty, string.Empty);
            var english = Catalog("Confirm", "Cancel", "Help");
            var finnish = Catalog("Vahvista", string.Empty, string.Empty);
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "template.pot"), template);
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "en.po"), english);
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "fi.po"), finnish);

            var locks = Substitute.For<ILocalTempFileLocks>();
            locks.GetTempFilePath($"translationRepos/{project.Id}").Returns(checkout.FullName);
            locks.LockAsync(checkout.FullName, CancellationToken.None)
                .Returns(new ValueTask<IDisposable>(Substitute.For<IDisposable>()));
            var repository = Substitute.For<TranslationRepositoryOperations>();
            repository.PrepareCheckout(project.RepositoryFullName, project.SourceBranch, checkout.FullName,
                CancellationToken.None).Returns("initial-sha");
            var import = new ImportTranslationProjectJob(NullLogger<ImportTranslationProjectJob>.Instance, database,
                locks, Substitute.For<IBackgroundJobClient>(), repository);

            // Start with configuration only: the actual import must discover both languages and all values.
            Assert.Empty(await database.TranslationUnits.ToListAsync());
            Assert.Empty(await database.TranslationValues.ToListAsync());
            Assert.Empty(await database.TranslationProjectLanguages.ToListAsync());
            await import.Execute(project.Id, CancellationToken.None);
            Assert.Equal("initial-sha", project.LastImportedSha);
            Assert.Equal(new[] { "en", "fi" }, await database.TranslationProjectLanguages
                .OrderBy(language => language.LanguageCode).Select(language => language.LanguageCode).ToArrayAsync());
            Assert.Equal(3, await database.TranslationUnits.CountAsync());
            Assert.Equal(6, await database.TranslationValues.CountAsync());
            var confirm = await database.TranslationUnits.FindAsync(project.Id, "CONFIRM_BUTTON");
            Assert.NotNull(confirm);
            Assert.Equal("Confirm", confirm.SourceText);
            var source = await Value(database, project.Id, "CONFIRM_BUTTON", "en");
            var target = await Value(database, project.Id, "CONFIRM_BUTTON", "fi");
            Assert.Equal("Confirm", source.Text);
            Assert.Equal("Vahvista", target.Text);
            Assert.Equal(TranslationState.Translated, target.State);
            foreach (var key in new[] { "CANCEL_BUTTON", "HELP_BUTTON" })
            {
                var empty = await Value(database, project.Id, key, "fi");
                Assert.Equal(string.Empty, empty.Text);
                Assert.Equal(TranslationState.NeedsTranslation, empty.State);
            }

            var context = new DefaultHttpContext();
            context.Items[AppInfo.CurrentUserMiddlewareKey] = user;
            var editor = new TranslationEditorController(NullLogger<TranslationEditorController>.Instance, database)
            {
                ControllerContext = new ControllerContext { HttpContext = context },
            };

            // Exercise the same saves as the UI, including invalidation after a source edit.
            var editedSource = await editor.Save(new TranslationSaveRequest
            {
                ProjectId = project.Id, Key = confirm.Key, LanguageCode = "en",
                Text = "Confirm selection", Version = source.Version,
            });
            Assert.NotNull(editedSource.Value);
            Assert.Equal("Confirm selection", editedSource.Value.Text);
            Assert.Equal(2, confirm.SourceRevision);
            Assert.Equal(TranslationState.NeedsTranslation, target.State);
            Assert.NotNull(source.PendingRevision);
            var editedTarget = await editor.Save(new TranslationSaveRequest
            {
                ProjectId = project.Id, Key = confirm.Key, LanguageCode = "fi",
                Text = "Vahvista valinta", Version = target.Version,
            });
            Assert.NotNull(editedTarget.Value);
            Assert.Equal("Vahvista valinta", editedTarget.Value.Text);
            Assert.Equal("Confirm selection", target.SourceTextTranslatedFrom);
            Assert.Equal(confirm.SourceRevision, target.SourceRevisionTranslatedFrom);
            Assert.Equal(TranslationState.Translated, target.State);
            var sourceRevision = source.PendingRevision;
            var targetRevision = target.PendingRevision;
            Assert.NotNull(targetRevision);

            // Git still has the old English and confirmation texts but gains an unrelated Finnish translation.
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "fi.po"),
                Catalog("Vahvista", "Peruuta", string.Empty));
            repository.PrepareCheckout(project.RepositoryFullName, project.SourceBranch, checkout.FullName,
                CancellationToken.None).Returns("updated-upstream-sha");
            await import.Execute(project.Id, CancellationToken.None);
            Assert.Equal("updated-upstream-sha", project.LastImportedSha);
            Assert.Empty(await database.TranslationImportConflicts.ToListAsync());
            Assert.Equal("Confirm selection", confirm.SourceText);
            Assert.Equal(2, confirm.SourceRevision);
            Assert.Equal("Confirm selection", source.Text);
            Assert.Equal("Confirm", source.LastImportedText);
            Assert.Equal("Vahvista valinta", target.Text);
            Assert.Equal("Vahvista", target.LastImportedText);
            Assert.Equal(sourceRevision, source.PendingRevision);
            Assert.Equal(targetRevision, target.PendingRevision);
            var cancel = await Value(database, project.Id, "CANCEL_BUTTON", "fi");
            Assert.Equal("Peruuta", cancel.Text);
            Assert.Equal("Peruuta", cancel.LastImportedText);
            Assert.Equal(TranslationState.Translated, cancel.State);
            Assert.Null(cancel.PendingRevision);

            var github = Substitute.For<ITranslationGithubClient>();
            repository.CreateGithubClient("test-token").Returns(github);
            repository.GetCurrentCommit(checkout.FullName, CancellationToken.None).Returns("published-head-sha");
            github.GetLogin(CancellationToken.None).Returns("translation-bot");
            github.CreatePullRequest(project.RepositoryFullName, project.SourceBranch, "translation-bot",
                Arg.Any<string>(), CancellationToken.None).Returns(new TranslationGithubPullRequest(42, true,
                "published-head-sha", "unused", "translation-bot", "main", true));
            var tokens = Substitute.For<ITranslationGithubTokenProvider>();
            tokens.GetToken(CancellationToken.None).Returns("test-token");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:FromAddress"] = "translations@example.com",
            }).Build();
            var publish = new PublishTranslationProjectJob(database, locks, tokens, configuration,
                NullLogger<PublishTranslationProjectJob>.Instance, repository);
            await publish.Execute(project.Id, CancellationToken.None);

            var finalEnglish = await File.ReadAllTextAsync(Path.Combine(checkout.FullName, "en.po"));
            var finalFinnish = await File.ReadAllTextAsync(Path.Combine(checkout.FullName, "fi.po"));
            var englishEntries = GettextTranslationParser.Parse(template, finalEnglish, finalEnglish)
                .ToDictionary(entry => entry.Key);
            var finnishEntries = GettextTranslationParser.Parse(template, finalEnglish, finalFinnish)
                .ToDictionary(entry => entry.Key);
            Assert.Equal(3, englishEntries.Count);
            Assert.Equal(3, finnishEntries.Count);
            Assert.Equal("Confirm selection", englishEntries["CONFIRM_BUTTON"].TargetText);
            Assert.Equal("Cancel", englishEntries["CANCEL_BUTTON"].TargetText);
            Assert.Equal("Help", englishEntries["HELP_BUTTON"].TargetText);
            Assert.Equal("Confirm selection", finnishEntries["CONFIRM_BUTTON"].SourceText);
            Assert.Equal("Vahvista valinta", finnishEntries["CONFIRM_BUTTON"].TargetText);
            Assert.Equal("Peruuta", finnishEntries["CANCEL_BUTTON"].TargetText);
            Assert.Equal(string.Empty, finnishEntries["HELP_BUTTON"].TargetText);
            Assert.False(finnishEntries["CONFIRM_BUTTON"].Fuzzy);
            Assert.False(finnishEntries["CANCEL_BUTTON"].Fuzzy);
            Assert.True(finnishEntries["HELP_BUTTON"].Fuzzy);
            Assert.Equal(template, await File.ReadAllTextAsync(Path.Combine(checkout.FullName, "template.pot")));
            Assert.Null(source.PendingRevision);
            Assert.Null(target.PendingRevision);
            Assert.Equal(sourceRevision, source.LastPublishedRevision);
            Assert.Equal(targetRevision, target.LastPublishedRevision);
            var pr = Assert.Single(await database.TranslationPullRequests.ToListAsync());
            Assert.Equal(42, pr.Number);
            Assert.True(pr.Open);
            Assert.Equal("published-head-sha", pr.HeadSha);
            Assert.Equal(project.CurrentRevision, pr.LastPublishedRevision);
            Assert.Equal(new[] { "Import", "Import", "Publish" }, await database.TranslationSyncRuns
                .OrderBy(run => run.Id).Select(run => run.Type).ToArrayAsync());
            Assert.All(await database.TranslationSyncRuns.ToListAsync(), run =>
            {
                Assert.Equal("Succeeded", run.Status);
                Assert.NotNull(run.CompletedAt);
            });

            await repository.Received(1).CommitFile(checkout.FullName, "en.po", "Update en translations",
                Arg.Is<Dictionary<string, string>>(environment => environment["GIT_AUTHOR_NAME"] == user.UserName),
                CancellationToken.None);
            await repository.Received(1).CommitFile(checkout.FullName, "fi.po", "Update fi translations",
                Arg.Is<Dictionary<string, string>>(environment => environment["GIT_AUTHOR_NAME"] == user.UserName),
                CancellationToken.None);
            await repository.Received(1).PushBranch(checkout.FullName,
                "https://github.com/translation-bot/translations.git", pr.HeadBranch, string.Empty,
                Arg.Any<Dictionary<string, string>>(), CancellationToken.None);
        }
        finally
        {
            checkout.Delete(true);
        }
    }

    private static string Catalog(string confirm, string cancel, string help)
    {
        return $"""
                msgid ""
                msgstr ""
                "Content-Type: text/plain; charset=UTF-8\n"

                #: Buttons.cs:1
                msgid "CONFIRM_BUTTON"
                msgstr "{confirm}"

                msgid "CANCEL_BUTTON"
                msgstr "{cancel}"

                msgid "HELP_BUTTON"
                msgstr "{help}"

                """;
    }

    private static async Task<TranslationValue> Value(ApplicationDbContext database, long projectId, string key,
        string language)
    {
        var value = await database.TranslationValues.FindAsync(projectId, key, language);
        Assert.NotNull(value);
        return value;
    }
}
