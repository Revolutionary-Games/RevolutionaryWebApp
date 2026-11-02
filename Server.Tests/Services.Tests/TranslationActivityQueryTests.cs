namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared.Models;
using Xunit;

public class TranslationActivityQueryTests
{
    [Theory]
    [InlineData(TranslationActivityView.All)]
    [InlineData(TranslationActivityView.Edits)]
    [InlineData(TranslationActivityView.Comments)]
    [InlineData(TranslationActivityView.Keys)]
    [InlineData(TranslationActivityView.Source)]
    public void ActivityViewsTranslateToPagedPostgresQueries(TranslationActivityView view)
    {
        using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused").Options);
        var project = new TranslationProject { Id = 1, SourceLanguage = "en" };
        var query = TranslationActivityQuery.Create(database, project, "fi", view).Skip(50).Take(50);

        // SQL generation exercises PostgreSQL translation without opening a database connection.
        var sql = query.ToQueryString();
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("LIMIT", sql);
        Assert.Contains("OFFSET", sql);
    }

    [Fact]
    public void MixedProjectActivityWithActorFilterTranslatesToPostgres()
    {
        using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused").Options);
        var project = new TranslationProject { Id = 1, SourceLanguage = "en" };
        var sql = TranslationActivityQuery.Create(database, project, null,
                [TranslationActivityView.Comments, TranslationActivityView.Source], 42)
            .Skip(50).Take(50).ToQueryString();
        Assert.Contains("UNION ALL", sql);
        Assert.Contains("LIMIT", sql);
    }

    [Fact]
    public async Task NewKeysWithTheSameCreationTimeHaveStablePagesWithoutNumericIds()
    {
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var project = new TranslationProject { Id = 1, SourceLanguage = "en" };
        database.TranslationProjects.Add(project);
        var createdAt = DateTime.UtcNow;
        for (var index = 60; index >= 0; --index)
        {
            database.TranslationUnits.Add(new TranslationUnit
            {
                ProjectId = project.Id, Key = $"key{index:D3}", CreatedAt = createdAt,
            });
        }

        await database.SaveChangesAsync();
        var query = TranslationActivityQuery.Create(database, project, "fi", TranslationActivityView.Keys);
        var first = await query.Take(50).ToArrayAsync();
        var second = await query.Skip(50).Take(50).ToArrayAsync();
        Assert.Equal(50, first.Length);
        Assert.Equal(11, second.Length);
        Assert.Equal(Enumerable.Range(0, 61).Select(index => $"key{index:D3}"),
            first.Concat(second).Select(item => item.Key));
    }
}
