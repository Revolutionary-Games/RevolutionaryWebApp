namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
using Xunit.Abstractions;

public class TranslationSyncRealDbTests : IClassFixture<RealUnitTestDatabaseFixture>
{
    private readonly RealUnitTestDatabaseFixture fixture;
    private readonly ITestOutputHelper output;

    public TranslationSyncRealDbTests(RealUnitTestDatabaseFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        this.output = output;
    }

    [Theory]
    [InlineData(10, 2)]
    [InlineData(500, 10)]
    public async Task ImportQueryCountDoesNotGrowWithKeysOrLanguages(int keyCount, int languageCount)
    {
        var counter = new SelectCounter();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.GetConnectionStringForAdditionalDatabase()).UseSnakeCaseNamingConvention()
            .AddInterceptors(counter).Options;
        await using var setup = new ApplicationDbContext(options);
        var project = new TranslationProject
        {
            Slug = "sync-performance-" + Guid.NewGuid(), Name = "Sync performance test",
            Format = TranslationFormat.Gettext,
            RepositoryFullName = "example/translations", SourceBranch = "main", SourceFilePattern = "template.pot",
            TranslationFilePattern = "{language}.po",
        };
        setup.TranslationProjects.Add(project);
        await setup.SaveChangesAsync();
        var checkout = Directory.CreateTempSubdirectory("translation-sync-scale-test-");
        try
        {
            var template = new StringBuilder();
            var source = new StringBuilder();
            var target = new StringBuilder();
            for (var key = 0; key < keyCount; ++key)
            {
                template.Append($"msgid \"KEY_{key}\"\nmsgstr \"\"\n\n");
                source.Append($"msgid \"KEY_{key}\"\nmsgstr \"Source {key}\"\n\n");
                target.Append($"msgid \"KEY_{key}\"\nmsgstr \"Target {key}\"\n\n");
            }

            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "template.pot"), template.ToString());
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "en.po"), source.ToString());
            for (var language = 0; language < languageCount; ++language)
            {
                await File.WriteAllTextAsync(Path.Combine(checkout.FullName, $"target{language}.po"),
                    target.ToString());
            }

            var locks = Substitute.For<ILocalTempFileLocks>();
            locks.GetTempFilePath($"translationRepos/{project.Id}").Returns(checkout.FullName);
            locks.LockAsync(checkout.FullName, CancellationToken.None)
                .Returns(new ValueTask<IDisposable>(Substitute.For<IDisposable>()));
            var repository = Substitute.For<TranslationRepositoryOperations>();
            for (var replay = 0; replay < 2; ++replay)
            {
                repository.PrepareCheckout(project.RepositoryFullName, project.SourceBranch, checkout.FullName,
                    CancellationToken.None).Returns("upstream-" + replay);
                await using var worker = new ApplicationDbContext(options);
                var job = new ImportTranslationProjectJob(NullLogger<ImportTranslationProjectJob>.Instance, worker,
                    locks, Substitute.For<IBackgroundJobClient>(), Substitute.For<IModelUpdateNotificationSender>(),
                    repository);
                counter.Count = 0;
                var timer = Stopwatch.StartNew();
                await job.Execute(project.Id, CancellationToken.None);
                timer.Stop();
                var queryCount = counter.Count;
                output.WriteLine($"{keyCount} keys, {languageCount} target languages, import {replay + 1}: " +
                    $"{queryCount} SELECTs, {timer.Elapsed.TotalSeconds:F2} seconds");

                Assert.InRange(queryCount, 1, 15);
                Assert.Equal(keyCount * (languageCount + 1), await worker.TranslationValues.IgnoreQueryFilters()
                    .CountAsync(value => value.ProjectId == project.Id));
                Assert.All(await worker.TranslationValues.IgnoreQueryFilters().AsNoTracking().Where(value =>
                        value.ProjectId == project.Id && value.LanguageCode == "target0").ToListAsync(),
                    value => Assert.Equal("Target " + value.Key[4..], value.Text));
            }

            await MeasurePublish(options, project, checkout.FullName, locks, repository);
        }
        finally
        {
            checkout.Delete(true);
            await setup.TranslationPullRequests.IgnoreQueryFilters().Where(pr => pr.ProjectId == project.Id)
                .ExecuteDeleteAsync();
            await setup.TranslationSyncRuns.IgnoreQueryFilters().Where(run => run.ProjectId == project.Id)
                .ExecuteDeleteAsync();
            await setup.TranslationChanges.IgnoreQueryFilters().Where(change => change.ProjectId == project.Id)
                .ExecuteDeleteAsync();
            await setup.TranslationValues.IgnoreQueryFilters().Where(value => value.ProjectId == project.Id)
                .ExecuteDeleteAsync();
            await setup.TranslationUnits.IgnoreQueryFilters().Where(unit => unit.ProjectId == project.Id)
                .ExecuteDeleteAsync();
            await setup.TranslationProjectLanguages.IgnoreQueryFilters()
                .Where(language => language.ProjectId == project.Id)
                .ExecuteDeleteAsync();
            await setup.TranslationProjects.Where(item => item.Id == project.Id).ExecuteDeleteAsync();
        }
    }

    private async Task MeasurePublish(DbContextOptions<ApplicationDbContext> options, TranslationProject project,
        string checkout, ILocalTempFileLocks locks, TranslationRepositoryOperations repository)
    {
        await using (var editor = new ApplicationDbContext(options))
        {
            var currentProject = await editor.TranslationProjects.SingleAsync(item => item.Id == project.Id);
            var values = await editor.TranslationValues.Where(value => value.ProjectId == project.Id &&
                value.Key == "KEY_0" && value.LanguageCode != "en").ToListAsync();
            foreach (var value in values)
            {
                value.Text = "Local edit";
                value.PendingRevision = ++currentProject.CurrentRevision;
                value.PendingSince = DateTime.UtcNow;
                editor.TranslationChanges.Add(new TranslationChange
                {
                    ProjectId = project.Id, Key = value.Key, LanguageCode = value.LanguageCode,
                    Kind = TranslationChangeKind.UserEdit, OldText = value.LastImportedText, NewText = value.Text,
                    OldState = value.State, NewState = value.State, ProjectRevision = currentProject.CurrentRevision,
                });
            }

            await editor.SaveChangesAsync();
        }

        var editingLocked = false;
        var lockedTime = new Stopwatch();
        var sender = Substitute.For<IModelUpdateNotificationSender>();
        sender.SendNotifications(Arg.Any<IEnumerable<Tuple<SerializedNotification, string>>>()).Returns(_ =>
        {
            editingLocked = !editingLocked;
            if (editingLocked)
            {
                lockedTime.Start();
            }
            else
            {
                lockedTime.Stop();
            }

            return Task.CompletedTask;
        });
        var github = Substitute.For<ITranslationGithubClient>();
        repository.CreateGithubClient("token").Returns(github);
        repository.GetCurrentCommit(checkout, CancellationToken.None).Returns("published-head");
        github.GetLogin(CancellationToken.None).Returns("bot");
        github.CreatePullRequest(project.RepositoryFullName!, project.SourceBranch!, "bot", Arg.Any<string>(),
            Arg.Any<string>(), CancellationToken.None).Returns(new TranslationGithubPullRequest(42, true,
            "published-head", "unused", "bot", "main", true));
        repository.CommitFile(checkout, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Dictionary<string, string>>(),
            CancellationToken.None).Returns(_ =>
        {
            Assert.False(editingLocked);
            return Task.CompletedTask;
        });
        var tokens = Substitute.For<ITranslationGithubTokenProvider>();
        tokens.GetToken(CancellationToken.None).Returns("token");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BaseUrl"] = "https://example.com/", ["Email:FromAddress"] = "translations@example.com",
        }).Build();
        await using var worker = new ApplicationDbContext(options);
        var job = new PublishTranslationProjectJob(worker, locks, tokens, configuration,
            NullLogger<PublishTranslationProjectJob>.Instance, sender, repository);
        var timer = Stopwatch.StartNew();
        await job.Execute(project.Id, CancellationToken.None);
        timer.Stop();
        output.WriteLine($"Publish: {timer.Elapsed.TotalSeconds:F2} seconds total, " +
            $"{lockedTime.Elapsed.TotalSeconds:F2} seconds with editing locked (Git/GitHub mocked)");
        Assert.False(editingLocked);
        Assert.False(await worker.TranslationValues.IgnoreQueryFilters()
            .AnyAsync(value => value.ProjectId == project.Id && value.PendingRevision != null));
        Assert.Equal("published-head", (await worker.TranslationPullRequests.SingleAsync(pr =>
            pr.ProjectId == project.Id)).HeadSha);
    }

    private sealed class SelectCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal))
                ++Count;
            return ValueTask.FromResult(result);
        }
    }
}
