namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
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
    [Theory]
    [InlineData(TranslationFormat.Gettext, false)]
    [InlineData(TranslationFormat.Resx, false)]
    [InlineData(TranslationFormat.Gettext, true)]
    [InlineData(TranslationFormat.Resx, true)]
    [InlineData(TranslationFormat.Gettext, false, "王小明")]
    [InlineData(TranslationFormat.Gettext, true, "𠀀小明")]
    public async Task ImportEditsAndRepublishingPreserveCatalogsAndTranslatorAttribution(TranslationFormat format,
        bool republishWithAutomatedChanges, string displayName = "Source translator")
    {
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IModelUpdateNotificationSender>());
        var extension = format == TranslationFormat.Gettext ? "po" : "resx";
        var project = new TranslationProject
        {
            Name = "First deployment",
            Slug = "first-deployment",
            Format = format,
            RepositoryFullName = "example/translations",
            SourceBranch = "main",
            SourceFilePattern = format == TranslationFormat.Gettext ? "template.pot" : "en.resx",
            TranslationFilePattern = "{language}." + extension,
        };
        var user = new User("translator@example.com", "Translator") { DisplayName = displayName };
        user.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User, GroupType.SourceTextTranslator));
        var secondUser = new User("second@example.com", "Second translator")
        {
            UseEmailInTranslationCredits = true,
        };
        secondUser.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User));
        database.TranslationProjects.Add(project);
        database.Users.Add(user);
        database.Users.Add(secondUser);
        await database.SaveChangesAsync();

        var checkout = Directory.CreateTempSubdirectory("translation-workflow-test-");
        try
        {
            var template = Catalog(format, string.Empty, string.Empty, string.Empty);
            var english = Catalog(format, "Confirm", "Cancel", "Help");
            var german = Catalog(format, "Bestätigen", "Abbrechen", "Hilfe");
            var swedish = Catalog(format, "Bekräfta", "Avbryt", "Hjälp");
            var finnish = Catalog(format, "Vahvista", string.Empty, string.Empty);
            if (format == TranslationFormat.Gettext)
                await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "template.pot"), template);
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "en." + extension), english);
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "fi." + extension), finnish);

            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "de." + extension), german);
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "sv." + extension), swedish);

            var locks = Substitute.For<ILocalTempFileLocks>();
            locks.GetTempFilePath($"translationRepos/{project.Id}").Returns(checkout.FullName);
            locks.LockAsync(checkout.FullName, CancellationToken.None)
                .Returns(new ValueTask<IDisposable>(Substitute.For<IDisposable>()));
            var repository = Substitute.For<TranslationRepositoryOperations>();
            repository.PrepareCheckout(project.RepositoryFullName, project.SourceBranch, checkout.FullName,
                CancellationToken.None).Returns("initial-sha");
            var import = new ImportTranslationProjectJob(NullLogger<ImportTranslationProjectJob>.Instance, database,
                locks, Substitute.For<IBackgroundJobClient>(), Substitute.For<IModelUpdateNotificationSender>(),
                repository);

            // Start with configuration only: the actual import must discover both languages and all values.
            Assert.Empty(await database.TranslationUnits.ToListAsync());
            Assert.Empty(await database.TranslationValues.ToListAsync());
            Assert.Empty(await database.TranslationProjectLanguages.ToListAsync());
            await import.Execute(project.Id, CancellationToken.None);
            Assert.Equal("initial-sha", project.LastImportedSha);
            Assert.Equal(new[] { "de", "en", "fi", "sv" }, await database.TranslationProjectLanguages
                .OrderBy(language => language.LanguageCode).Select(language => language.LanguageCode).ToArrayAsync());
            Assert.Equal(3, await database.TranslationUnits.CountAsync());
            Assert.Equal(12, await database.TranslationValues.CountAsync());
            var confirm = await database.TranslationUnits.FindAsync(project.Id, "CONFIRM_BUTTON");
            Assert.NotNull(confirm);
            Assert.Equal("Confirm", confirm.SourceText);

            // This earlier contribution is already in upstream and must not receive credit again.
            database.TranslationChanges.Add(new TranslationChange
            {
                ProjectId = project.Id, Key = confirm.Key, LanguageCode = "en",
                ContributorUserId = secondUser.Id, ActorUserId = secondUser.Id,
                Kind = TranslationChangeKind.UserEdit, OldText = "Legacy confirmation", NewText = "Confirm",
                OldState = TranslationState.Translated, NewState = TranslationState.Translated,
                CountsAsContribution = true,
            });
            await database.SaveChangesAsync();

            Assert.Equal(new[] { "CONFIRM_BUTTON", "CANCEL_BUTTON", "HELP_BUTTON" },
                await database.TranslationUnits.OrderBy(unit => unit.Position).Select(unit => unit.Key).ToArrayAsync());
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

            var initialVersion = source.Version;
            var explanationOnly = await editor.Save(new TranslationSaveRequest
            {
                ProjectId = project.Id, Key = confirm.Key, LanguageCode = "en",
                Text = source.Text, State = source.State, Version = source.Version,
                Explanation = "Confirms the current selection.",
            });
            Assert.Equal("Confirms the current selection.", explanationOnly.Value!.Explanation);
            Assert.NotEqual(initialVersion, source.Version);
            Assert.Equal(1, confirm.SourceRevision);
            Assert.Equal(TranslationState.Translated, target.State);
            Assert.Null(source.PendingRevision);

            // Exercise the same saves as the UI, including invalidation after a source edit.
            var editedSource = await editor.Save(new TranslationSaveRequest
            {
                ProjectId = project.Id, Key = confirm.Key, LanguageCode = "en",
                Text = "Confirm selection", Explanation = "Confirms the current selection.", Version = source.Version,
            });
            Assert.NotNull(editedSource.Value);
            Assert.Equal("Confirm selection", editedSource.Value.Text);
            Assert.Equal("Confirms the current selection.", editedSource.Value.Explanation);
            Assert.Equal(2, confirm.SourceRevision);
            Assert.Equal(TranslationState.NeedsTranslation, target.State);
            Assert.NotNull(source.PendingRevision);
            context.Items[AppInfo.CurrentUserMiddlewareKey] = secondUser;
            var editedTarget = await editor.Save(new TranslationSaveRequest
            {
                ProjectId = project.Id, Key = confirm.Key, LanguageCode = "fi",
                Text = "Vahvista valinta", Explanation = "Target editors cannot replace this.",
                Version = target.Version,
            });
            Assert.NotNull(editedTarget.Value);
            Assert.Equal("Vahvista valinta", editedTarget.Value.Text);
            Assert.Equal("Confirms the current selection.", editedTarget.Value.Explanation);
            Assert.Equal("Confirm selection", target.SourceTextTranslatedFrom);
            Assert.Equal(confirm.SourceRevision, target.SourceRevisionTranslatedFrom);
            Assert.Equal(TranslationState.Translated, target.State);
            var sourceRevision = source.PendingRevision;
            var targetRevision = target.PendingRevision;
            Assert.NotNull(targetRevision);

            // Git still has the old English and confirmation texts but gains an unrelated Finnish translation.
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "fi." + extension),
                Catalog(format, "Vahvista", "Peruuta", string.Empty));
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
                Arg.Any<string>(), Arg.Any<string>(), CancellationToken.None).Returns(new TranslationGithubPullRequest(
                42, true, "published-head-sha", "unused", "translation-bot", "main", true));
            var tokens = Substitute.For<ITranslationGithubTokenProvider>();
            tokens.GetToken(CancellationToken.None).Returns("test-token");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:FromAddress"] = "translations@example.com",
                ["BaseUrl"] = "https://devcenter.example.com/",
            }).Build();
            var publish = new PublishTranslationProjectJob(database, locks, tokens, configuration,
                NullLogger<PublishTranslationProjectJob>.Instance, Substitute.For<IModelUpdateNotificationSender>(),
                repository);
            await publish.Execute(project.Id, CancellationToken.None);

            await github.Received(1).CreatePullRequest(project.RepositoryFullName, project.SourceBranch,
                "translation-bot", Arg.Any<string>(), "https://devcenter.example.com/translations/first-deployment",
                CancellationToken.None);

            var finalEnglish = await File.ReadAllTextAsync(Path.Combine(checkout.FullName, "en." + extension));
            var finalFinnish = await File.ReadAllTextAsync(Path.Combine(checkout.FullName, "fi." + extension));
            var englishEntries = ParseCatalog(format, template, finalEnglish, finalEnglish);
            var finnishEntries = ParseCatalog(format, template, finalEnglish, finalFinnish);
            Assert.Equal(3, englishEntries.Count);
            Assert.Equal(3, finnishEntries.Count);
            Assert.Equal("Confirm selection", englishEntries["CONFIRM_BUTTON"].TargetText);
            Assert.Equal("Cancel", englishEntries["CANCEL_BUTTON"].TargetText);
            Assert.Equal("Help", englishEntries["HELP_BUTTON"].TargetText);
            Assert.Equal("Confirm selection", finnishEntries["CONFIRM_BUTTON"].SourceText);
            Assert.Equal("Vahvista valinta", finnishEntries["CONFIRM_BUTTON"].TargetText);
            Assert.Equal("Peruuta", finnishEntries["CANCEL_BUTTON"].TargetText);
            Assert.Equal(string.Empty, finnishEntries["HELP_BUTTON"].TargetText);
            if (format == TranslationFormat.Gettext)
            {
                var gettextEntries = GettextTranslationParser.Parse(template, finalEnglish, finalFinnish)
                    .ToDictionary(entry => entry.Key);
                Assert.False(gettextEntries["CONFIRM_BUTTON"].Fuzzy);
                Assert.False(gettextEntries["CANCEL_BUTTON"].Fuzzy);
                Assert.False(gettextEntries["HELP_BUTTON"].Fuzzy);
                Assert.Contains($"\"Last-Translator: {displayName} <noreply+{user.Id}@example.com>\\n\"",
                    finalEnglish);
                Assert.DoesNotContain(user.Email, finalEnglish);
                Assert.Contains("\"Last-Translator: Second translator <second@example.com>\\n\"", finalFinnish);

                // Source invalidation changes German flags without a credited translator.
                var finalGerman = await File.ReadAllTextAsync(Path.Combine(checkout.FullName, "de.po"));
                Assert.True(GettextTranslationParser.Parse(template, finalEnglish, finalGerman)
                    .Single(entry => entry.Key == "CONFIRM_BUTTON").Fuzzy);
                Assert.Contains("\"Last-Translator: Previous translator <previous@example.com>\\n\"", finalGerman);
                Assert.DoesNotContain("DevCenter Translations", finalGerman);

                foreach (var language in new[] { "en", "fi" })
                {
                    var latestChange = await database.TranslationChanges.Where(change =>
                        change.ProjectId == project.Id && change.Key == "CONFIRM_BUTTON" &&
                        change.LanguageCode == language).MaxAsync(change => change.CreatedAt);
                    Assert.Contains("\"PO-Revision-Date: " +
                        latestChange.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "+0000\\n\"",
                        language == "en" ? finalEnglish : finalFinnish);
                }

                Assert.Equal(template, await File.ReadAllTextAsync(Path.Combine(checkout.FullName, "template.pot")));
            }
            else
            {
                Assert.Equal(new[] { "de.resx", "en.resx", "fi.resx", "sv.resx" }, checkout.GetFiles()
                    .Select(file => file.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
            }

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

            await repository.Received(1).CommitFile(checkout.FullName, "en." + extension, "Update en translations",
                Arg.Is<Dictionary<string, string>>(environment => environment["GIT_AUTHOR_NAME"] == user.DisplayName),
                CancellationToken.None);
            await repository.Received(1).CommitFile(checkout.FullName, "fi." + extension, "Update fi translations",
                Arg.Is<Dictionary<string, string>>(environment =>
                    environment["GIT_AUTHOR_NAME"] == secondUser.UserName),
                CancellationToken.None);
            if (format == TranslationFormat.Gettext)
            {
                await repository.Received(1).CommitFile(checkout.FullName, ".", "Update automated translation states",
                    Arg.Any<Dictionary<string, string>>(), CancellationToken.None);
                await repository.DidNotReceive().CommitFile(checkout.FullName, "de.po", Arg.Any<string>(),
                    Arg.Any<Dictionary<string, string>>(), CancellationToken.None);
                await repository.DidNotReceive().CommitFile(checkout.FullName, "sv.po", Arg.Any<string>(),
                    Arg.Any<Dictionary<string, string>>(), CancellationToken.None);
            }

            await repository.Received(1).PushBranch(checkout.FullName,
                "https://github.com/translation-bot/translations.git", pr.HeadBranch, string.Empty,
                Arg.Any<Dictionary<string, string>>(), CancellationToken.None);

            if (republishWithAutomatedChanges)
            {
                // A fresh checkout contains upstream, not the two commits already delivered to the fork.
                await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "en." + extension), english);
                await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "fi." + extension),
                    Catalog(format, "Vahvista", "Peruuta", string.Empty));
                await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "de." + extension), german);
                await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "sv." + extension), swedish);
                await import.Execute(project.Id, CancellationToken.None);
                Assert.Null(source.PendingRevision);
                Assert.Null(target.PendingRevision);

                // Automated state changes affect both files which still contain unmerged human edits.
                foreach (var value in new[] { source, target })
                {
                    var oldState = value.State;
                    value.State = TranslationState.NeedsTranslation;
                    value.PendingRevision = ++project.CurrentRevision;
                    database.TranslationChanges.Add(new TranslationChange
                    {
                        ProjectId = project.Id, Key = value.Key, LanguageCode = value.LanguageCode,
                        Kind = TranslationChangeKind.SourceChanged, OldText = value.Text, NewText = value.Text,
                        OldState = oldState, NewState = value.State, ProjectRevision = project.CurrentRevision,
                    });
                }

                await database.SaveChangesAsync();
                github.GetPullRequest(project.RepositoryFullName, pr.Number, CancellationToken.None)
                    .Returns(new TranslationGithubPullRequest(pr.Number, true, pr.HeadSha!, pr.HeadBranch,
                        "translation-bot", project.SourceBranch, true));
                repository.ClearReceivedCalls();
                github.ClearReceivedCalls();

                await publish.Execute(project.Id, CancellationToken.None);

                await repository.Received(1).CheckoutNewBranch(checkout.FullName, pr.HeadBranch,
                    project.SourceBranch, CancellationToken.None);
                await repository.Received(1).CommitFile(checkout.FullName, "en." + extension,
                    "Update en translations", Arg.Is<Dictionary<string, string>>(environment =>
                        environment["GIT_AUTHOR_NAME"] == user.DisplayName), CancellationToken.None);
                await repository.Received(1).CommitFile(checkout.FullName, "fi." + extension,
                    "Update fi translations", Arg.Is<Dictionary<string, string>>(environment =>
                        environment["GIT_AUTHOR_NAME"] == secondUser.UserName), CancellationToken.None);
                if (format == TranslationFormat.Resx)
                {
                    await repository.DidNotReceive().CommitFile(checkout.FullName, ".", Arg.Any<string>(),
                        Arg.Any<Dictionary<string, string>>(), CancellationToken.None);
                }
                else
                {
                    await repository.Received(1).CommitFile(checkout.FullName, ".",
                        "Update automated translation states", Arg.Any<Dictionary<string, string>>(),
                        CancellationToken.None);
                }

                await github.DidNotReceive().CreatePullRequest(Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), CancellationToken.None);
                var republished = ParseCatalog(format, template,
                    await File.ReadAllTextAsync(Path.Combine(checkout.FullName, "en." + extension)),
                    await File.ReadAllTextAsync(Path.Combine(checkout.FullName, "fi." + extension)));
                Assert.Equal("Confirm selection", republished["CONFIRM_BUTTON"].SourceText);
                Assert.Equal("Vahvista valinta", republished["CONFIRM_BUTTON"].TargetText);
                Assert.Null(source.PendingRevision);
                Assert.Null(target.PendingRevision);
            }
        }
        finally
        {
            checkout.Delete(true);
        }
    }

    private static string Catalog(TranslationFormat format, string confirm, string cancel, string help)
    {
        if (format == TranslationFormat.Resx)
        {
            return $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <root>
                      <data name="CONFIRM_BUTTON" xml:space="preserve">
                        <value>{confirm}</value>
                        <comment>Confirm the current selection</comment>
                      </data>
                      <data name="CANCEL_BUTTON" xml:space="preserve"><value>{cancel}</value></data>
                      <data name="HELP_BUTTON" xml:space="preserve"><value>{help}</value></data>
                    </root>
                    """;
        }

        return $"""
                msgid ""
                msgstr ""
                "Content-Type: text/plain; charset=UTF-8\n"
                "Last-Translator: Previous translator <previous@example.com>\n"

                #: Buttons.cs:1
                msgid "CONFIRM_BUTTON"
                msgstr "{confirm}"

                msgid "CANCEL_BUTTON"
                msgstr "{cancel}"

                msgid "HELP_BUTTON"
                msgstr "{help}"

                """;
    }

    private static Dictionary<string, (string SourceText, string TargetText)> ParseCatalog(TranslationFormat format,
        string template, string source, string target)
    {
        return format == TranslationFormat.Gettext ?
            GettextTranslationParser.Parse(template, source, target)
                .ToDictionary(entry => entry.Key, entry => (entry.SourceText, entry.TargetText)) :
            ResxTranslationParser.Parse(source, target)
                .ToDictionary(entry => entry.Key, entry => (entry.SourceText, entry.TargetText));
    }

    private static async Task<TranslationValue> Value(ApplicationDbContext database, long projectId, string key,
        string language)
    {
        var value = await database.TranslationValues.FindAsync(projectId, key, language);
        Assert.NotNull(value);
        return value;
    }
}
