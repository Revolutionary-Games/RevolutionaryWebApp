namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared.Models;

    /// <summary>
    ///   Creates the default folders in the database, if missing. This is used instead of seeding the db to make
    ///   importing previous data work better.
    /// </summary>
    public class CreateDefaultFoldersJob : IJob
    {
        private const string DevBuildFolderName = "DevBuild files";
        private const string CIFolderName = "CI";

        private readonly ILogger<CreateDefaultFoldersJob> logger;
        private readonly ApplicationDbContext database;
        private readonly IBackgroundJobClient jobClient;

        public CreateDefaultFoldersJob(ILogger<CreateDefaultFoldersJob> logger, ApplicationDbContext database,
            IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            var itemsToRecompute = new List<StorageItem?>
            {
                await CreateDefaultFolder("Trash", null, FileAccess.Developer, FileAccess.Nobody,
                    cancellationToken),
            };

            var builds = await CreateDefaultFolder(DevBuildFolderName, null, FileAccess.User, FileAccess.Nobody,
                cancellationToken);

            itemsToRecompute.Add(builds);

            if (builds == null)
            {
                // Required for later item adds...
                builds = await FindFolder(DevBuildFolderName, null, cancellationToken);

                if (builds == null)
                    throw new NullReferenceException("devbuilds folder failed to be retrieved");
            }

            itemsToRecompute.Add(await CreateDefaultFolder("Objects", builds, FileAccess.User, FileAccess.Nobody,
                cancellationToken));
            itemsToRecompute.Add(await CreateDefaultFolder("Dehydrated", builds, FileAccess.User, FileAccess.Nobody,
                cancellationToken));

            itemsToRecompute.Add(await CreateDefaultFolder("Public", null, FileAccess.Public, FileAccess.Developer,
                cancellationToken));

            itemsToRecompute.Add(await CreateDefaultFolder("Symbols", null, FileAccess.Developer, FileAccess.Nobody,
                cancellationToken));

            var ci = await CreateDefaultFolder(CIFolderName, null, FileAccess.Developer, FileAccess.Developer,
                cancellationToken);

            itemsToRecompute.Add(ci);

            if (ci == null)
            {
                ci = await FindFolder(CIFolderName, null, cancellationToken);

                if (ci == null)
                    throw new NullReferenceException("ci folder failed to be retrieved");
            }

            itemsToRecompute.Add(await CreateDefaultFolder("Images", ci, FileAccess.Developer, FileAccess.Developer,
                cancellationToken));

            await database.SaveChangesAsync(cancellationToken);

            // Queue jobs to count items in the new folders
            foreach (var item in itemsToRecompute)
            {
                if (item == null)
                    continue;

                jobClient.Enqueue<CountFolderItemsJob>(x => x.Execute(item.Id, CancellationToken.None));
            }
        }

        private Task<StorageItem?> FindFolder(string name, StorageItem? parent, CancellationToken cancellationToken)
        {
            return database.StorageItems.FirstOrDefaultAsync(
                i => i.Name == name && i.Ftype == FileType.Folder && i.Parent == parent, cancellationToken);
        }

        private async Task<StorageItem?> CreateDefaultFolder(string name, StorageItem? parent, FileAccess read,
            FileAccess write, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            var existing = await FindFolder(name, parent, cancellationToken);

            if (existing == null)
            {
                logger.LogInformation("Created default folder \"{Name}\"", name);

                var newItem = new StorageItem()
                {
                    Name = name,
                    Parent = parent,
                    AllowParentless = parent == null,
                    Ftype = FileType.Folder,
                    ReadAccess = read,
                    WriteAccess = write,
                    Special = true,
                };

                await database.StorageItems.AddAsync(newItem, cancellationToken);
                return newItem;
            }

            // Update access if incorrect
            if (existing.ReadAccess != read || existing.WriteAccess != write || existing.Special != true)
            {
                logger.LogInformation("Correcting incorrect access on default folder \"{Name}\"", name);
                existing.ReadAccess = read;
                existing.WriteAccess = write;
                existing.Special = true;
            }

            return null;
        }
    }
}
