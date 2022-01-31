namespace ThriveDevCenter.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Utilities;

    public class StackwalkSymbolPreparer : IStackwalkSymbolPreparer
    {
        private static readonly SemaphoreSlim GlobalSymbolPrepareLock = new(1);

        private readonly ILogger<StackwalkSymbolPreparer> logger;
        private readonly ApplicationDbContext database;
        private readonly IGeneralRemoteDownloadUrls downloadUrls;

        private readonly HttpClient httpClient = new();

        public StackwalkSymbolPreparer(ILogger<StackwalkSymbolPreparer> logger, ApplicationDbContext database,
            IGeneralRemoteDownloadUrls downloadUrls)
        {
            this.logger = logger;
            this.database = database;
            this.downloadUrls = downloadUrls;
        }

        public async Task PrepareSymbolsInFolder(string baseFolder, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(baseFolder))
            {
                logger.LogInformation("Debug symbols base folder is not configured, not handling symbols");
                return;
            }

            var lockTask = GlobalSymbolPrepareLock.WaitAsync(cancellationToken);

            var wantedSymbols = await database.DebugSymbols.Include(s => s.StoredInItem).Where(s => s.Active)
                .ToListAsync(cancellationToken);

            await lockTask;
            try
            {
                Directory.CreateDirectory(baseFolder);
                await HandleSymbols(baseFolder, wantedSymbols, cancellationToken);
            }
            finally
            {
                GlobalSymbolPrepareLock.Release();
            }
        }

        private async Task HandleSymbols(string baseFolder, List<DebugSymbol> wantedSymbols,
            CancellationToken cancellationToken)
        {
            if (wantedSymbols.Count < 1)
                return;

            if (!downloadUrls.Configured)
                throw new Exception("Download URLs are not configured, we can't download the symbols");

            foreach (var symbol in wantedSymbols)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var finalPath = Path.Combine(baseFolder, symbol.RelativePath);

                if (File.Exists(finalPath))
                    continue;

                if (symbol.StoredInItem == null)
                    throw new NotLoadedModelNavigationException();

                var version = await symbol.StoredInItem.GetHighestUploadedVersion(database);

                if (version == null)
                    throw new NullReferenceException("No highest uploaded version for a debug symbol");

                if (version.StorageFile == null)
                    throw new NotLoadedModelNavigationException();

                var tempFile = finalPath + ".tmp";

                logger.LogInformation("Downloading missing debug symbol {RelativePath}", symbol.RelativePath);

                var response = await httpClient.GetAsync(downloadUrls.CreateDownloadFor(version.StorageFile,
                    AppInfo.RemoteStorageDownloadExpireTime), cancellationToken);

                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStreamAsync(cancellationToken);

                // Make sure the directory we want to write to exists
                Directory.CreateDirectory(Path.GetDirectoryName(tempFile) ??
                    throw new Exception("Failed to get parent folder for the symbol file to write"));

                try
                {
                    await using var writer = File.OpenWrite(tempFile);
                    await content.CopyToAsync(writer, cancellationToken);
                }
                catch (OperationCanceledException e)
                {
                    logger.LogWarning(e, "Write to symbol file canceled, attempting to delete temp file");
                    File.Delete(tempFile);
                    throw;
                }

                File.Move(tempFile, finalPath);
                logger.LogInformation("Downloaded symbol {Id}", symbol.Id);
            }

            PruneExtraneousFiles(baseFolder, wantedSymbols);
        }

        private void PruneExtraneousFiles(string baseFolder, List<DebugSymbol> wantedSymbols)
        {
            var toDelete = DirectoryHelpers.GetFilesAndDirectoriesThatShouldNotExist(baseFolder,
                wantedSymbols.Select(s => s.RelativePath).ToList());

            foreach (var path in toDelete)
            {
                logger.LogInformation("Deleting extraneous file/directory from symbols folder: {Path}", path);

                if (Directory.Exists(path))
                {
                    Directory.Delete(path);
                }
                else
                {
                    File.Delete(path);
                }
            }
        }
    }

    public interface IStackwalkSymbolPreparer
    {
        Task PrepareSymbolsInFolder(string baseFolder, CancellationToken cancellationToken);
    }
}
