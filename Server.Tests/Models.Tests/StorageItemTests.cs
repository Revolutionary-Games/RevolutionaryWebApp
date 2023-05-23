namespace ThriveDevCenter.Server.Tests.Models.Tests;

using System;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Server.Models;
using Xunit;

public class StorageItemTests
{
    private static readonly Lazy<ApplicationDbContext> UniqueNameReadOnlyContext = new(CreateUniqueNamingDatabase);

    [Theory]
    [InlineData("First", "First_4")]
    [InlineData("First_2", "First_4")]
    [InlineData("First2", "First2")]
    [InlineData("Second", "Second_2")]
    [InlineData("file.png", "file_2.png")]
    [InlineData("other.png", "other.png")]
    [InlineData("multi_under_line_hard_file_2.png", "multi_under_line_hard_file_3.png")]
    public async Task StorageItem_UniqueNameGenerationWorks(string startingName, string expectedResult)
    {
        var database = UniqueNameReadOnlyContext.Value;

        var item = new StorageItem
        {
            Id = 22,
            Name = startingName,
        };

        await item.MakeNameUniqueInFolder(database);

        Assert.Equal(expectedResult, item.Name);
    }

    [Fact]
    public async Task StorageItem_AlreadyUniqueNameIsNotAffected()
    {
        var database = UniqueNameReadOnlyContext.Value;

        var item = new StorageItem
        {
            Id = 22,
            Name = "Unique",
        };

        await item.MakeNameUniqueInFolder(database);

        Assert.Equal("Unique", item.Name);
    }

    private static ApplicationDbContext CreateUniqueNamingDatabase()
    {
        return CreateDatabase("StorageItemUniqueNamingTest").Result;
    }

    private static async Task<ApplicationDbContext> CreateDatabase(string testName)
    {
        var database = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(testName).Options);

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = 100,
            Name = "First",
            Ftype = FileType.File,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = 101,
            Name = "First_2",
            Ftype = FileType.File,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = 102,
            Name = "First_3",
            Ftype = FileType.File,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = 103,
            Name = "First4",
            Ftype = FileType.File,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = 104,
            Name = "Second",
            Ftype = FileType.File,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = 105,
            Name = "file.png",
            Ftype = FileType.File,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = 106,
            Name = "multi_under_line_hard_file_2.png",
            Ftype = FileType.File,
        });

        await database.SaveChangesAsync();

        return database;
    }
}
