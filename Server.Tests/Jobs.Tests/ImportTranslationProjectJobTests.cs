namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Jobs;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared.Models;
using Xunit;

public class ImportTranslationProjectJobTests
{
    [Theory]
    [InlineData(TranslationFormat.Gettext, false)]
    [InlineData(TranslationFormat.Gettext, true)]
    [InlineData(TranslationFormat.Resx, false)]
    [InlineData(TranslationFormat.Resx, true)]
    public async Task DivergentSourceEditCreatesOneConflictAcrossLanguagesAndRepeatedImports(
        TranslationFormat format, bool published)
    {
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var extension = format == TranslationFormat.Gettext ? "po" : "resx";
        var project = new TranslationProject
        {
            Id = 1,
            Name = "Test project",
            Slug = "test-project",
            Format = format,
            SourceFilePattern = format == TranslationFormat.Gettext ? "template.pot" : "en.resx",
            TranslationFilePattern = "{language}." + extension,
            CurrentRevision = 5,
        };
        var unit = new TranslationUnit
        {
            ProjectId = project.Id, Key = "HELLO", SourceText = "Local edit", SourceRevision = 2,
        };
        var sourceValue = new TranslationValue
        {
            ProjectId = project.Id,
            Key = unit.Key,
            LanguageCode = project.SourceLanguage,
            Text = "Local edit",
            LastImportedText = "Old upstream",
            State = TranslationState.Translated,
            PendingRevision = published ? null : 5,
            LastPublishedRevision = published ? 5 : null,
        };
        database.TranslationProjects.Add(project);
        database.TranslationUnits.Add(unit);
        database.TranslationValues.Add(sourceValue);
        await database.SaveChangesAsync();

        var jobs = Substitute.For<IBackgroundJobClient>();
        var job = new ImportTranslationProjectJob(NullLogger<ImportTranslationProjectJob>.Instance, database,
            Substitute.For<ILocalTempFileLocks>(), jobs);
        var checkout = Directory.CreateTempSubdirectory("translation-import-test-");
        try
        {
            var source = format == TranslationFormat.Gettext ?
                "msgid \"HELLO\"\nmsgstr \"Other upstream edit\"\n" :
                "<root><data name=\"HELLO\"><value>Other upstream edit</value></data></root>";
            var target = format == TranslationFormat.Gettext ?
                "msgid \"HELLO\"\nmsgstr \"Translation\"\n" :
                "<root><data name=\"HELLO\"><value>Translation</value></data></root>";
            await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "en." + extension), source);
            if (format == TranslationFormat.Gettext)
            {
                await File.WriteAllTextAsync(Path.Combine(checkout.FullName, "template.pot"),
                    "msgid \"HELLO\"\nmsgstr \"\"\n");
            }

            for (var language = 0; language < 10; ++language)
            {
                await File.WriteAllTextAsync(Path.Combine(checkout.FullName, $"target{language}.{extension}"),
                    target);
            }

            // Exercise the file import without cloning or fetching a repository from GitHub.
            var importFiles = typeof(ImportTranslationProjectJob).GetMethod("ImportFiles",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(importFiles);
            for (var replay = 0; replay < 2; ++replay)
            {
                await (Task)importFiles.Invoke(job,
                    [project, checkout.FullName, "upstream-sha", CancellationToken.None])!;

                var conflict = Assert.Single(await database.TranslationImportConflicts.ToListAsync());
                Assert.Equal(project.SourceLanguage, conflict.LanguageCode);
                Assert.Equal("Local edit", conflict.LocalText);
                Assert.Equal("Other upstream edit", conflict.IncomingText);
                Assert.Null(conflict.ResolvedAt);
                Assert.Equal("Local edit", sourceValue.Text);
                Assert.Equal("Old upstream", sourceValue.LastImportedText);
                Assert.Equal("Local edit", unit.SourceText);
                Assert.Equal(2, unit.SourceRevision);
                Assert.Equal(11, await database.TranslationValues.CountAsync());
            }

            jobs.Received(1).Create(Arg.Any<Job>(), Arg.Any<IState>());
        }
        finally
        {
            checkout.Delete(true);
        }
    }
}
