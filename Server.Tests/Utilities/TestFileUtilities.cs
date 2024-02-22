namespace RevolutionaryWebApp.Server.Tests.Utilities;

using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Server.Models;

public static class TestFileUtilities
{
    public const long TestTrashFolderId = 400;

    public static async Task CreateTrashFolder(ApplicationDbContext database, long id = TestTrashFolderId)
    {
        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = id,
            Name = "Trash",
            Ftype = FileType.Folder,
            ReadAccess = FileAccess.RestrictedUser,
            WriteAccess = FileAccess.Nobody,
            Special = true,
        });
    }
}
