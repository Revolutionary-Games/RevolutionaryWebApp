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
    private static readonly Lazy<ApplicationDbContext> FileStructureReadOnlyContext = new(CreateFileStructureDatabase);

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

    [Theory]
    [InlineData(100, "StorageTestParent")]
    [InlineData(101, "StorageTestParent/EmptyFolder")]
    [InlineData(102, "StorageTestParent/NonEmpty")]
    [InlineData(103, "StorageTestParent/NonEmpty/Name1")]
    [InlineData(104, "ItemInRoot")]
    [InlineData(107, "StorageTestParent/NonEmpty/Name1/Just a file")]
    public async Task StorageItem_ComputeStoragePathWorks(long fileId, string expectedPath)
    {
        var database = FileStructureReadOnlyContext.Value;

        var item = await database.StorageItems.FindAsync(fileId);

        Assert.NotNull(item);

        var path = await item.ComputeStoragePath(database);

        Assert.Equal(expectedPath, path);

        var item2 = await database.StorageItems.Include(i => i.Parent).FirstOrDefaultAsync(i => i.Id == fileId);
        Assert.NotNull(item2);

        Assert.Equal(expectedPath, await item2.ComputeStoragePath(database));
    }

    private static ApplicationDbContext CreateUniqueNamingDatabase()
    {
        return CreateUniqueNamingDatabaseItems("StorageItemUniqueNamingTest").Result;
    }

    private static async Task<ApplicationDbContext> CreateUniqueNamingDatabaseItems(string testName)
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

    private static ApplicationDbContext CreateFileStructureDatabase()
    {
        return CreateFileStructureDatabaseItems("StorageItemStructureROTest").Result;
    }

    private static async Task<ApplicationDbContext> CreateFileStructureDatabaseItems(string testName)
    {
        var database = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(testName).Options);

        var parentFolder = new StorageItem
        {
            Id = 100,
            Name = "StorageTestParent",
            Ftype = FileType.Folder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
        };

        await database.StorageItems.AddAsync(parentFolder);

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = 101,
            Name = "EmptyFolder",
            Ftype = FileType.Folder,
            Parent = parentFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
        });

        var nonEmptyFolder = new StorageItem
        {
            Id = 102,
            Name = "NonEmpty",
            Ftype = FileType.Folder,
            Parent = parentFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
        };

        await database.StorageItems.AddAsync(nonEmptyFolder);

        var folderInNonEmpty = new StorageItem
        {
            Id = 103,
            Name = "Name1",
            Ftype = FileType.Folder,
            Parent = nonEmptyFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
        };

        await database.StorageItems.AddAsync(folderInNonEmpty);

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = 104,
            Name = "ItemInRoot",
            Ftype = FileType.File,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = 105,
            Name = "DevOnly",
            Ftype = FileType.Folder,
            Parent = parentFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.Developer,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = 106,
            Name = "Special",
            Ftype = FileType.Folder,
            Parent = parentFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.Nobody,
            Special = true,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = 107,
            Name = "Just a file",
            Ftype = FileType.Folder,
            Parent = folderInNonEmpty,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.Nobody,
            Special = true,
        });

        await database.SaveChangesAsync();

        return database;
    }
}
