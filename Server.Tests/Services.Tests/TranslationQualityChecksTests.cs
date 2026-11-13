namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared.Models;
using Xunit;

public class TranslationQualityChecksTests
{
    [Theory]
    [InlineData("Hello {0}, {1}!", "Hei {1}, {0}!", false)]
    [InlineData("Hello {0}, {1}!", "Hei {0}!", true)]
    [InlineData("Value {0,10:N2}", "Arvo {0:N3}", false)]
    [InlineData("Value {0,-10:N2}", "Arvo", true)]
    [InlineData("Literal {{0}}", "Teksti", false)]
    [InlineData("Value {{{0}}}", "Arvo {{0}}", true)]
    [InlineData("Value {{{0}}}", "Arvo {0}", false)]
    [InlineData("Value {0}", "Arvo {00}", false)]
    [InlineData("Value {0}", "Arvo {{0}}", true)]
    public void FormattingChecksArgumentsAndSkipsEscapedBraces(string source, string translation, bool fails)
    {
        var problems = TranslationQualityChecks.Evaluate(source, translation, TranslationQualityCheck.CSharpFormatting);
        Assert.Equal(fails, problems.ContainsKey(TranslationProblemKind.MissingFormatPlaceholder));
    }

    [Theory]
    [InlineData("Hello!", "Hei。", false, false)]
    [InlineData("Hello!", "hei!", true, false)]
    [InlineData(" Hello!\n", " Hei?  ", false, false)]
    [InlineData("hello", "Hei", false, false)]
    [InlineData("Hello", "Hei!", false, true)]
    [InlineData("Hello!", "Hei", false, true)]
    [InlineData("Hello…", "Hei؟", false, false)]
    [InlineData("Hello", "Hei🙂", false, false)]
    [InlineData("𐐀 hello!", "𐐨 hei!", true, false)]
    [InlineData("Abilities", "能力", false, false)]
    [InlineData("Abilities", "能力（测试）", false, true)]
    [InlineData("Abilities", "能力ABC", false, false)]
    [InlineData("Abilities", "アビリティ", false, false)]
    [InlineData("Abilities", "능력", false, false)]
    [InlineData("Abilities", "قدرات", false, false)]
    [InlineData("Abilities", "יכולות", false, false)]
    [InlineData("Abilities", "ความสามารถ", false, false)]
    [InlineData("Abilities", "क्षमताएँ", false, false)]
    [InlineData("Abilities", "способности", true, false)]
    [InlineData("Abilities", "ικανότητες", true, false)]
    [InlineData("Abilities", "ǅungla", false, false)]
    public void SentenceChecksUnicodeCapitalizationAndPresenceOfPunctuation(string source, string translation,
        bool capitalProblem, bool punctuationProblem)
    {
        var problems = TranslationQualityChecks.Evaluate(source, translation,
            TranslationQualityCheck.SentenceConsistency);
        Assert.Equal(capitalProblem, problems.ContainsKey(TranslationProblemKind.InitialCapitalization));
        Assert.Equal(punctuationProblem, problems.ContainsKey(TranslationProblemKind.EndingPunctuation));
    }

    [Fact]
    public void EmptyTranslationsAndDisabledChecksHaveNoProblems()
    {
        Assert.Empty(TranslationQualityChecks.Evaluate("Hello {0}!", " ",
            TranslationQualityCheck.CSharpFormatting | TranslationQualityCheck.SentenceConsistency));
        Assert.Empty(TranslationQualityChecks.Evaluate("Hello {0}!", "hei", TranslationQualityCheck.None));
    }

    [Fact]
    public async Task WarningsAreIdempotentPreserveIgnoresAndDisappearWhenFixedOrDisabled()
    {
        await using var database = CreateDatabase();
        var value = AddValue(database, "key", "Hello {0}!", "hei", DateTime.UtcNow);
        var project = value.Unit.Project;
        TranslationQualityChecks.UpdateProblems(database, project, value);
        await database.SaveChangesAsync();
        Assert.Equal(3, await database.TranslationProblems.CountAsync());
        var ignored = value.Problems.Single(problem => problem.Kind == TranslationProblemKind.InitialCapitalization);
        ignored.Ignored = true;
        ignored.IgnoredByUser = new User("translator@example.com", "Translator");
        ignored.IgnoredAt = DateTime.UtcNow.AddHours(-1);
        await database.SaveChangesAsync();
        var ignoredBy = ignored.IgnoredByUserId;
        var ignoredAt = ignored.IgnoredAt;

        TranslationQualityChecks.UpdateProblems(database, project, value);
        await database.SaveChangesAsync();
        Assert.Equal(3, await database.TranslationProblems.CountAsync());
        Assert.True(ignored.Ignored);
        Assert.Equal(ignoredBy, ignored.IgnoredByUserId);
        Assert.Equal(ignoredAt, ignored.IgnoredAt);
        Assert.Null(value.PendingRevision);
        Assert.Equal(TranslationState.Translated, value.State);

        value.Text = "Hei {0}!";
        TranslationQualityChecks.UpdateProblems(database, project, value);
        await database.SaveChangesAsync();
        Assert.Empty(database.TranslationProblems);

        value.Text = "hei";
        TranslationQualityChecks.UpdateProblems(database, project, value);
        await database.SaveChangesAsync();
        Assert.All(database.TranslationProblems, problem =>
        {
            Assert.False(problem.Ignored);
            Assert.Null(problem.IgnoredByUserId);
            Assert.Null(problem.IgnoredAt);
        });
        project.QualityChecks = TranslationQualityCheck.None;
        TranslationQualityChecks.UpdateProblems(database, project, value);
        await database.SaveChangesAsync();
        Assert.Empty(database.TranslationProblems);
    }

    [Theory]
    [InlineData(TranslationState.Translated)]
    [InlineData(TranslationState.Verified)]
    public async Task NeedsChangesSkipsChecksClearsWarningsAndChecksAgainWhenReady(TranslationState readyState)
    {
        await using var database = CreateDatabase();
        var value = AddValue(database, "key", "Hello {0}!", "hei", DateTime.UtcNow);
        var project = value.Unit.Project;
        value.State = TranslationState.NeedsTranslation;
        TranslationQualityChecks.UpdateProblems(database, project, value);
        await database.SaveChangesAsync();
        Assert.Empty(database.TranslationProblems);

        value.State = readyState;
        TranslationQualityChecks.UpdateProblems(database, project, value);
        await database.SaveChangesAsync();
        Assert.Equal(3, await database.TranslationProblems.CountAsync());

        value.State = TranslationState.NeedsTranslation;
        TranslationQualityChecks.UpdateProblems(database, project, value);
        await database.SaveChangesAsync();
        Assert.Empty(value.Problems);
        Assert.Empty(database.TranslationProblems);
    }

    [Fact]
    public async Task ChangedMissingArgumentsRequireReviewAgain()
    {
        await using var database = CreateDatabase();
        var value = AddValue(database, "key", "Hello {0} {1}!", "Hei {0}!", DateTime.UtcNow);
        TranslationQualityChecks.UpdateProblems(database, value.Unit.Project, value);
        await database.SaveChangesAsync();
        var warning = Assert.Single(value.Problems);
        warning.Ignored = true;
        warning.IgnoredByUser = new User("translator@example.com", "Translator");
        warning.IgnoredAt = DateTime.UtcNow;
        await database.SaveChangesAsync();
        value.Text = "Hei {1}!";
        TranslationQualityChecks.UpdateProblems(database, value.Unit.Project, value);
        await database.SaveChangesAsync();
        Assert.False(warning.Ignored);
        Assert.Null(warning.IgnoredByUserId);
        Assert.Null(warning.IgnoredByUser);
        Assert.Null(warning.IgnoredAt);
        Assert.Contains("{0}", warning.Message);
    }

    [Fact]
    public async Task DailyJobChecksRecentEditsImportsAndSourceChangesAndCleansOldWarnings()
    {
        await using var database = CreateDatabase();
        var old = DateTime.UtcNow.AddDays(-10);
        AddValue(database, "recent", "Hello!", "hei", DateTime.UtcNow.AddHours(-71));
        var imported = AddValue(database, "imported", "Hello!", "hei", old);
        imported.LastEditedAt = DateTime.UtcNow.AddHours(-1);
        var sourceChanged = AddValue(database, "sourceChanged", "Hello!", "hei", old);
        sourceChanged.Unit.SourceUpdatedAt = DateTime.UtcNow;
        AddValue(database, "old", "Hello!", "hei", old);
        var fixedValue = AddValue(database, "fixed", "Hello!", "Hei!", old);
        fixedValue.Problems.Add(new TranslationProblem
        {
            ProjectId = 1, Key = fixedValue.Key, LanguageCode = "fi",
            Kind = TranslationProblemKind.InitialCapitalization,
        });
        var inactive = AddValue(database, "inactive", "Hello!", "hei", DateTime.UtcNow);
        inactive.Unit.Active = false;
        var needsChanges = AddValue(database, "needsChanges", "Hello!", "hei", DateTime.UtcNow);
        needsChanges.State = TranslationState.NeedsTranslation;
        var oldNeedsChanges = AddValue(database, "oldNeedsChanges", "Hello!", "hei", old);
        oldNeedsChanges.State = TranslationState.NeedsTranslation;
        oldNeedsChanges.Problems.Add(new TranslationProblem
        {
            ProjectId = 1, Key = oldNeedsChanges.Key, LanguageCode = "fi",
            Kind = TranslationProblemKind.InitialCapitalization,
        });
        var source = AddValue(database, "source", "Hello!", "hei", DateTime.UtcNow);
        source.LanguageCode = "en";
        source.Language = new TranslationProjectLanguage { Project = source.Unit.Project, LanguageCode = "en" };
        await database.SaveChangesAsync();

        await new CheckTranslationQualityJob(database).Execute(CancellationToken.None);

        Assert.Equal(6, await database.TranslationProblems.CountAsync());
        Assert.All(await database.TranslationProblems.ToListAsync(), problem =>
            Assert.Contains(problem.Key, new[] { "recent", "imported", "sourceChanged" }));
    }

    [Fact]
    public async Task CleanupDoesNotSkipBatchesWhenOldWarningsDisappear()
    {
        await using var database = CreateDatabase();
        var old = DateTime.UtcNow.AddDays(-10);
        for (var index = 0; index < 205; ++index)
        {
            var value = AddValue(database, $"key{index:D3}", "Hello!", "Hei!", old);
            value.Problems.Add(new TranslationProblem
            {
                ProjectId = 1, Key = value.Key, LanguageCode = "fi",
                Kind = TranslationProblemKind.InitialCapitalization,
            });
        }

        await database.SaveChangesAsync();
        await new CheckTranslationQualityJob(database).Execute(CancellationToken.None);
        Assert.Empty(database.TranslationProblems);
    }

    [Fact]
    public async Task FullRecheckIncludesOlderTextAndClearsDisabledWarnings()
    {
        await using var database = CreateDatabase();
        AddValue(database, "old", "Hello!", "hei", DateTime.UtcNow.AddDays(-10));
        await database.SaveChangesAsync();
        var job = new CheckTranslationQualityJob(database);
        await job.ExecuteForProject(1, true, CancellationToken.None);
        Assert.Equal(2, await database.TranslationProblems.CountAsync());
        var project = await database.TranslationProjects.SingleAsync();
        project.QualityChecks = TranslationQualityCheck.None;
        await database.SaveChangesAsync();
        await job.ExecuteForProject(1, true, CancellationToken.None);
        Assert.Empty(database.TranslationProblems);
    }

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }

    private static TranslationValue AddValue(ApplicationDbContext database, string key, string source, string text,
        DateTime changedAt)
    {
        var project = database.TranslationProjects.Local.FirstOrDefault() ?? new TranslationProject
        {
            Id = 1, Slug = "test", SourceLanguage = "en", Format = TranslationFormat.Manual,
            QualityChecks = TranslationQualityCheck.CSharpFormatting | TranslationQualityCheck.SentenceConsistency,
        };
        var language = database.TranslationProjectLanguages.Local.FirstOrDefault() ?? new TranslationProjectLanguage
        {
            Project = project, LanguageCode = "fi",
        };
        var value = new TranslationValue
        {
            ProjectId = 1, Key = key, LanguageCode = "fi", Text = text, Language = language,
            CreatedAt = changedAt, LastEditedAt = changedAt, State = TranslationState.Translated,
            Unit = new TranslationUnit
            {
                Project = project, Key = key, SourceText = source, SourceUpdatedAt = changedAt,
            },
        };
        database.TranslationValues.Add(value);
        return value;
    }
}
