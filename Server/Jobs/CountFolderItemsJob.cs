namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Models;

    public class CountFolderItemsJob
    {
        private readonly ApplicationDbContext database;

        public CountFolderItemsJob(ApplicationDbContext database)
        {
            this.database = database;
        }

        public async Task Execute(long folderId, CancellationToken cancellationToken)
        {
            var folder = await database.StorageItems.FindAsync(folderId);

            if (folder == null)
                throw new NullReferenceException("couldn't find folder to count items in");

            folder.Size = await database.StorageItems.CountAsync(i => i.ParentId == folder.Id, cancellationToken);

            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
