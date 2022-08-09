namespace ThriveDevCenter.Server.Tests.Jobs.Tests;

using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Server.Jobs;
using Server.Models;
using Shared.Models;
using Xunit;

public class CountFolderItemsTests : IClassFixture<RealUnitTestDatabaseFixture>
{
    private readonly RealUnitTestDatabaseFixture fixture;

    public CountFolderItemsTests(RealUnitTestDatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task CountFolderItems_WorksForFolders()
    {
        var database = fixture.Database;
        await using var transaction = await database.Database.BeginTransactionAsync();

        var folder1 = new StorageItem()
        {
            Name = "folder1",
            Ftype = FileType.Folder,
            AllowParentless = true,
        };

        var folder2 = new StorageItem()
        {
            Name = "folder2",
            Ftype = FileType.Folder,
            AllowParentless = true,
        };

        var folder3 = new StorageItem()
        {
            Name = "folder3",
            Ftype = FileType.Folder,
            AllowParentless = true,
        };

        var file1 = new StorageItem()
        {
            Name = "file1",
            Ftype = FileType.File,
            Parent = folder1,
        };

        var file2 = new StorageItem()
        {
            Name = "file2",
            Ftype = FileType.File,
            Parent = folder1,
        };

        var file3 = new StorageItem()
        {
            Name = "file3",
            Ftype = FileType.File,
            Parent = folder2,
        };

        await database.StorageItems.AddAsync(folder1);
        await database.StorageItems.AddAsync(folder2);
        await database.StorageItems.AddAsync(folder3);
        await database.StorageItems.AddAsync(file1);
        await database.StorageItems.AddAsync(file2);
        await database.StorageItems.AddAsync(file3);

        await database.SaveChangesAsync();

        var instance = new CountFolderItemsJob(database);

        Assert.Null(folder1.Size);
        Assert.Null(folder2.Size);
        Assert.Null(folder3.Size);

        await instance.Execute(folder1.Id, CancellationToken.None);

        Assert.Equal(2, folder1.Size);
        Assert.Null(folder2.Size);
        Assert.Null(folder3.Size);

        await instance.Execute(folder2.Id, CancellationToken.None);
        await instance.Execute(folder3.Id, CancellationToken.None);

        Assert.Equal(2, folder1.Size);
        Assert.Equal(1, folder2.Size);
        Assert.Equal(0, folder3.Size);
    }
}