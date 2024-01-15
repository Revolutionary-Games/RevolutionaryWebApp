namespace ThriveDevCenter.Server.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncKeyedLock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Utilities;

/// <summary>
///   Prepares the local filesystem for handling stackwalk operations
/// </summary>
public interface IStackwalkSymbolPreparer
{
    public Task PrepareSymbolsInFolder(string baseFolder, CancellationToken cancellationToken);
}

public class StackwalkSymbolPreparer : IStackwalkSymbolPreparer
{
    private static readonly AsyncNonKeyedLocker GlobalSymbolPrepareLock = new(1);

    private readonly ILogger<StackwalkSymbolPreparer> logger;
    private readonly ApplicationDbContext database;
    private readonly IGeneralRemoteDownloadUrls downloadUrls;
    private readonly IFileDownloader fileDownloader;

    public StackwalkSymbolPreparer(ILogger<StackwalkSymbolPreparer> logger, ApplicationDbContext database,
        IGeneralRemoteDownloadUrls downloadUrls, IFileDownloader fileDownloader)
    {
        this.logger = logger;
        this.database = database;
        this.downloadUrls = downloadUrls;
        this.fileDownloader = fileDownloader;
    }

    public async Task PrepareSymbolsInFolder(string baseFolder, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseFolder))
        {
            logger.LogInformation("Debug symbols base folder is not configured, not handling symbols");
            return;
        }

        var wantedSymbols = await database.DebugSymbols.Include(s => s.StoredInItem).Where(s => s.Active)
            .ToListAsync(cancellationToken);

        using (await GlobalSymbolPrepareLock.LockAsync(cancellationToken))
        {
            Directory.CreateDirectory(baseFolder);
            await HandleSymbols(baseFolder, wantedSymbols, cancellationToken);
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

            await fileDownloader.DownloadFile(downloadUrls.CreateDownloadFor(version.StorageFile,
                AppInfo.RemoteStorageDownloadExpireTime), tempFile, cancellationToken);

            File.Move(tempFile, finalPath);
            logger.LogInformation("Downloaded symbol {Id} to {FinalPath}", symbol.Id, finalPath);
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