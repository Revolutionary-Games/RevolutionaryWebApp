namespace ThriveDevCenter.Server.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Utilities;

public class BackupHandler
{
    private const string RedisDump = "dump.rdb";

    private readonly ILogger<BackupHandler> logger;
    private readonly BackupStorage storage;

    private readonly int backupsToKeep;
    private readonly string? redisPath;
    private readonly bool includeBlobs;
    private readonly string? databaseConnectionString;
    private readonly bool cleanBucket;

    public BackupHandler(ILogger<BackupHandler> logger, IConfiguration configuration, BackupStorage storage)
    {
        this.logger = logger;
        this.storage = storage;

        Configured = false;

        databaseConnectionString = configuration["ConnectionStrings:WebApiConnection"];

        if (!storage.Configured)
            return;

        if (!Convert.ToBoolean(configuration["Backup:Enabled"]))
            return;

        includeBlobs = Convert.ToBoolean(configuration["Backup:IncludeBlobs"]);
        backupsToKeep = Convert.ToInt32(configuration["Backup:BackupsToKeep"]);
        cleanBucket = Convert.ToBoolean(configuration["Backup:CleanBucketFromExtraFiles"]);
        UseXZCompression = Convert.ToBoolean(configuration["Backup:UseXZCompression"]);
        redisPath = configuration["Backup:RedisPath"];

        // TODO: allow skipping redis
        if (string.IsNullOrEmpty(redisPath) || backupsToKeep < 1 || string.IsNullOrEmpty(databaseConnectionString))
            return;

        if (!includeBlobs)
        {
            logger.LogInformation(
                "Database backup won't include blobs. This might leave out some pretty important stuff");
        }

        Configured = true;
    }

    public bool Configured { get; init; }

    public bool UseXZCompression { get; init; }

    public string GetDownloadUrlForBackup(Backup backup, TimeSpan? expiresIn = null)
    {
        ThrowIfNotConfigured();

        expiresIn ??= AppInfo.RemoteStorageDownloadExpireTime;

        return storage.CreatePreSignedDownloadURL(backup.Name, expiresIn.Value);
    }

    public async Task DeleteRemoteBackupFile(Backup backup)
    {
        ThrowIfNotConfigured();

        logger.LogInformation("Trying to delete path in backup bucket: {Name}", backup.Name);
        await storage.DeleteObject(backup.Name);
    }

    public Task UploadBackup(Backup backup, string backupFile, CancellationToken cancellationToken)
    {
        ThrowIfNotConfigured();

        logger.LogInformation("Uploading backup {Name}", backup.Name);
        return storage.UploadFile(backup.Name, File.OpenRead(backupFile),
            UseXZCompression ? AppInfo.TarXZMimeType : AppInfo.TarGZMimeType, cancellationToken);
    }

    public async Task DeleteExcessBackups(ApplicationDbContext database, CancellationToken cancellationToken,
        int maxBackupsToClear = 50)
    {
        ThrowIfNotConfigured();

        logger.LogInformation("Clearing excess backups to get under the limit of {BackupsToKeep}", backupsToKeep);

        var allBackups = await database.Backups.OrderByDescending(b => b.CreatedAt).ToListAsync(cancellationToken);

        // Detect what items to remove
        var backupsThatShouldExist = allBackups.Take(backupsToKeep).ToDictionary(i => i.Name, i => i);

        if (cleanBucket)
        {
            await DeleteWithBucketCleaning(database, cancellationToken, maxBackupsToClear, allBackups,
                backupsThatShouldExist);
        }
        else
        {
            await DeleteJustKnownItems(database, cancellationToken, maxBackupsToClear, allBackups,
                backupsThatShouldExist);
        }
    }

    public async Task DumpDatabaseToFile(string databaseFile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(databaseConnectionString))
            throw new InvalidOperationException("No database connection configured");

        var pgDump = ExecutableFinder.Which("pg_dump");

        if (pgDump == null)
            throw new Exception("pg_dump executable not found");

        logger.LogTrace("Dumping database to file: {DatabaseFile}", databaseFile);

        // I guess there isn't a better way than to manually write the parsing here
        var dbConnectionParameters = databaseConnectionString!.Split(";").Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Split('=').Select(v => v.Trim()).ToList())
            .ToDictionary(p => p[0], p => p[1]);

        var startInfo = new ProcessStartInfo(pgDump)
        {
            CreateNoWindow = true,
            WorkingDirectory = PathParser.GetParentPath(databaseFile),
        };

        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(databaseFile);
        startInfo.ArgumentList.Add("--clean");
        startInfo.ArgumentList.Add("--create");
        startInfo.ArgumentList.Add("--if-exists");

        startInfo.ArgumentList.Add("--dbname");
        startInfo.ArgumentList.Add(dbConnectionParameters["Database"]);

        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(dbConnectionParameters["Port"]);

        startInfo.ArgumentList.Add("--username");
        startInfo.ArgumentList.Add(dbConnectionParameters["User ID"]);

        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add(dbConnectionParameters["Server"]);

        startInfo.ArgumentList.Add(includeBlobs ? "--blobs" : "--no-blobs");

        startInfo.Environment["PGPASSWORD"] = dbConnectionParameters["Password"];

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);

        if (result.ExitCode != 0)
            throw new Exception($"pg_dump failed with code {result.ExitCode}, output: {result.FullOutput}");
    }

    public async Task CreateBackupTarFile(string backupFile, string databaseFileName,
        CancellationToken cancellationToken)
    {
        var tar = ExecutableFinder.Which("tar");

        if (tar == null)
            throw new Exception("tar executable not found");

        if (File.Exists(backupFile))
            File.Delete(backupFile);

        logger.LogTrace("Writing backup to file: {BackupFile}", backupFile);

        var startInfo = new ProcessStartInfo(tar)
        {
            CreateNoWindow = true,
            WorkingDirectory = PathParser.GetParentPath(backupFile),
        };

        startInfo.ArgumentList.Add("-cf");
        startInfo.ArgumentList.Add(backupFile);

        startInfo.ArgumentList.Add(UseXZCompression ? "-J" : "-z");

        startInfo.ArgumentList.Add(databaseFileName);

        if (!string.IsNullOrEmpty(redisPath))
            startInfo.ArgumentList.Add(Path.Join(redisPath, RedisDump));

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);

        if (result.ExitCode != 0)
            throw new Exception($"tar failed with code {result.ExitCode}, output: {result.FullOutput}");
    }

    protected void ThrowIfNotConfigured()
    {
        if (!Configured || redisPath == null)
        {
            throw new InvalidOperationException("Backups are not configured");
        }
    }

    private async Task DeleteWithBucketCleaning(ApplicationDbContext database, CancellationToken cancellationToken,
        int maxBackupsToClear, List<Backup> allBackups, Dictionary<string, Backup> backupsThatShouldExist)
    {
        logger.LogWarning("Backup bucket cleaning from extra files is not fully tested");

        int deletedBackups = 0;
        int deletedStorageItems = 0;

        foreach (var backup in allBackups)
        {
            if (backupsThatShouldExist.ContainsKey(backup.Name))
                continue;

            logger.LogInformation("Deleting backup {Name} to get under the limit of {BackupsToKeep}", backup.Name,
                backupsToKeep);

            database.Backups.Remove(backup);
            ++deletedBackups;

            if (deletedBackups >= maxBackupsToClear)
                break;
        }

        await database.SaveChangesAsync(cancellationToken);

        var existing = await storage.ListAllFiles(cancellationToken);

        foreach (var existingPath in existing)
        {
            // Don't touch things if there's accidentally something else in the bucket
            if (!existingPath.StartsWith("ThriveDevCenter"))
                continue;

            if (!backupsThatShouldExist.ContainsKey(existingPath))
            {
                logger.LogInformation("Backup path that");
            }

            logger.LogInformation(
                "Deleting backup item {ExistingPath} from remote storage as it is no longer related to any backup",
                existingPath);

            await storage.DeleteObject(existingPath);
            ++deletedStorageItems;

            if (deletedStorageItems >= maxBackupsToClear)
                break;

            cancellationToken.ThrowIfCancellationRequested();
        }

        logger.LogInformation(
            "Finished deleting old backups. Deleted: {DeletedBackups} and {DeletedStorageItems} remote items",
            deletedBackups, deletedStorageItems);
    }

    private async Task DeleteJustKnownItems(ApplicationDbContext database, CancellationToken cancellationToken,
        int maxBackupsToClear, List<Backup> allBackups, Dictionary<string, Backup> backupsThatShouldExist)
    {
        int deletedBackups = 0;

        foreach (var backup in allBackups)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (backupsThatShouldExist.ContainsKey(backup.Name))
                continue;

            logger.LogInformation("Deleting backup {Name} to get under the limit of {BackupsToKeep}", backup.Name,
                backupsToKeep);

            await DeleteRemoteBackupFile(backup);
            database.Backups.Remove(backup);
            ++deletedBackups;

            if (deletedBackups >= maxBackupsToClear)
                break;
        }

        // We have to save here to keep our info about what database items are deleted
        // ReSharper disable once MethodSupportsCancellation
        await database.SaveChangesAsync();

        logger.LogInformation("Finished deleting old backups, deleted: {DeletedBackups}", deletedBackups);
    }
}
