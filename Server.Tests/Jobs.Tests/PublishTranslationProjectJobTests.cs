namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Jobs;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared.Models;
using Shared.Notifications;
using Xunit;

public class PublishTranslationProjectJobTests
{
    [Theory]
    [InlineData(TranslationFormat.Gettext, false)]
    [InlineData(TranslationFormat.Gettext, true)]
    [InlineData(TranslationFormat.Resx, false)]
    [InlineData(TranslationFormat.Resx, true)]
    public async Task EditsDuringExportRemainPendingAndDoNotChangePublishedSnapshot(TranslationFormat format,
        bool changedFile)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ApplicationDbContext(options);
        var extension = format == TranslationFormat.Gettext ? "po" : "resx";
        var project = new TranslationProject
        {
            Id = 1, Slug = "test", Format = format, RepositoryFullName = "example/translations",
            SourceBranch = "main", SourceFilePattern = "en." + extension,
            TranslationFilePattern = "{language}." + extension, LastImportedSha = "upstream", CurrentRevision = 1,
        };
        database.TranslationProjects.Add(project);
        foreach (var code in new[] { "en", "fi", "sv", "de" })
        {
            database.TranslationProjectLanguages.Add(new TranslationProjectLanguage
            {
                ProjectId = project.Id, LanguageCode = code, Deleted = code == "sv", Enabled = code != "de",
            });
        }

        foreach (var code in new[] { "sv", "de" })
        {
            // Hidden languages must not be exported or marked as delivered, even with pending work.
            database.TranslationValues.Add(new TranslationValue
            {
                ProjectId = project.Id, Key = "HELLO", LanguageCode = code, Text = "Hidden edit",
                LastImportedText = "Upstream", State = TranslationState.Translated, PendingRevision = 1,
            });
        }

        foreach (var key in new[] { "HELLO", "OTHER" })
        {
            database.TranslationUnits.Add(new TranslationUnit
            {
                ProjectId = project.Id, Key = key, SourceText = "English",
            });
            database.TranslationValues.Add(new TranslationValue
            {
                ProjectId = project.Id, Key = key, LanguageCode = "en", Text = "English",
                LastImportedText = "English", State = TranslationState.Translated,
            });
            database.TranslationValues.Add(new TranslationValue
            {
                ProjectId = project.Id, Key = key, LanguageCode = "fi",
                Text = key == "HELLO" && changedFile ? "First edit" : "Upstream", LastImportedText = "Upstream",
                State = TranslationState.Translated, PendingRevision = key == "HELLO" ? 1 : null,
                PendingSince = key == "HELLO" ? DateTime.UtcNow : null,
            });
        }

        await database.SaveChangesAsync();
        var checkout = Directory.CreateTempSubdirectory("translation-publish-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "en." + extension),
                Catalog(format, "English"));
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "fi." + extension),
                Catalog(format, "Upstream"));
            var locks = Substitute.For<ILocalTempFileLocks>();
            locks.GetTempFilePath("translationRepos/1").Returns(checkout.FullName);
            locks.LockAsync(checkout.FullName, CancellationToken.None)
                .Returns(new ValueTask<IDisposable>(Substitute.For<IDisposable>()));
            var sender = Substitute.For<IModelUpdateNotificationSender>();
            var editingLocked = false;
            sender.SendNotifications(Arg.Any<IEnumerable<Tuple<SerializedNotification, string>>>())
                .Returns(_ =>
                {
                    editingLocked = !editingLocked;
                    return Task.CompletedTask;
                });

            var repository = Substitute.For<TranslationRepositoryOperations>();
            repository.PrepareCheckout(project.RepositoryFullName, project.SourceBranch, checkout.FullName,
                CancellationToken.None).Returns(_ =>
            {
                Assert.False(editingLocked);
                return "upstream";
            });
            repository.CheckoutNewBranch(checkout.FullName, Arg.Any<string>(), "main", CancellationToken.None)
                .Returns(async _ =>
                {
                    Assert.False(editingLocked);
                    await using var editor = new ApplicationDbContext(options);
                    var currentProject = await editor.TranslationProjects.SingleAsync();
                    foreach (var key in new[] { "HELLO", "OTHER" })
                    {
                        var current = await editor.TranslationValues.FindAsync(project.Id, key, "fi");
                        Assert.NotNull(current);
                        current.Text = "Later edit";
                        current.Version = Guid.NewGuid();
                        current.PendingRevision = ++currentProject.CurrentRevision;
                        current.PendingSince ??= DateTime.UtcNow;
                    }

                    await editor.SaveChangesAsync();
                });
            repository.GetCurrentCommit(checkout.FullName, CancellationToken.None).Returns("published-head");
            repository.CommitFile(checkout.FullName, Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<Dictionary<string, string>>(), CancellationToken.None).Returns(_ =>
            {
                Assert.False(editingLocked);
                return Task.CompletedTask;
            });
            var github = Substitute.For<ITranslationGithubClient>();
            repository.CreateGithubClient("token").Returns(github);
            github.GetLogin(CancellationToken.None).Returns(_ =>
            {
                Assert.False(editingLocked);
                return "bot";
            });
            github.CreatePullRequest(project.RepositoryFullName, "main", "bot", Arg.Any<string>(),
                Arg.Any<string>(), CancellationToken.None).Returns(new TranslationGithubPullRequest(42, true,
                "published-head", "unused", "bot", "main", true));
            var tokens = Substitute.For<ITranslationGithubTokenProvider>();
            tokens.GetToken(CancellationToken.None).Returns("token");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BaseUrl"] = "https://example.com/", ["Email:FromAddress"] = "translations@example.com",
            }).Build();
            var job = new PublishTranslationProjectJob(database, locks, tokens, configuration,
                NullLogger<PublishTranslationProjectJob>.Instance, sender, repository);

            await job.Execute(project.Id, CancellationToken.None);

            Assert.False(editingLocked);
            var output = await File.ReadAllTextAsync(Path.Combine(checkout.FullName, "fi." + extension));
            Assert.Contains(changedFile ? "First edit" : "Upstream", output);
            Assert.DoesNotContain("Later edit", output);
            var currentValues = await database.TranslationValues.AsNoTracking()
                .Where(value => value.LanguageCode == "fi").ToDictionaryAsync(value => value.Key);
            Assert.All(currentValues.Values, value => Assert.Equal("Later edit", value.Text));
            Assert.Equal(2, currentValues["HELLO"].PendingRevision);
            Assert.Equal(3, currentValues["OTHER"].PendingRevision);
            Assert.Equal(1, currentValues["HELLO"].LastPublishedRevision);
            Assert.Null(currentValues["OTHER"].LastPublishedRevision);
            Assert.Equal(3, (await database.TranslationProjects.AsNoTracking().SingleAsync()).CurrentRevision);
            Assert.All(await database.TranslationValues.IgnoreQueryFilters().AsNoTracking()
                .Where(value => value.LanguageCode == "sv" || value.LanguageCode == "de").ToListAsync(), value =>
            {
                Assert.Equal(1, value.PendingRevision);
                Assert.Null(value.LastPublishedRevision);
            });
            if (changedFile)
            {
                Assert.Equal(1, (await database.TranslationPullRequests.SingleAsync()).LastPublishedRevision);
            }
            else
            {
                Assert.Empty(await database.TranslationPullRequests.ToListAsync());
                await repository.DidNotReceive().PushBranch(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), Arg.Any<Dictionary<string, string>>(), CancellationToken.None);
            }
        }
        finally
        {
            checkout.Delete(true);
        }
    }

    private static string Catalog(TranslationFormat format, string text)
    {
        return format == TranslationFormat.Gettext ?
            $"msgid \"HELLO\"\nmsgstr \"{text}\"\n\nmsgid \"OTHER\"\nmsgstr \"{text}\"\n" :
            $"<root><data name=\"HELLO\"><value>{text}</value></data>" +
            $"<data name=\"OTHER\"><value>{text}</value></data></root>";
    }
}
