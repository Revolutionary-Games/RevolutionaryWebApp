namespace ThriveDevCenter.Server.Jobs;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Io;
using Hangfire;
using Microsoft.Extensions.Logging;
using Models;
using Services;

/// <summary>
///   Creates a new backup and makes sure there aren't too many backups by deleting existing ones
/// </summary>
[DisableConcurrentExecution(1000)]
public class CreateBackupJob : IJob
{
    private readonly ILogger<CreateBackupJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly BackupHandler backupHandler;
    private readonly ILocalTempFileLocks tempFileLocks;

    public CreateBackupJob(ILogger<CreateBackupJob> logger, NotificationsEnabledDb database,
        BackupHandler backupHandler, ILocalTempFileLocks tempFileLocks)
    {
        this.logger = logger;
        this.database = database;
        this.backupHandler = backupHandler;
        this.tempFileLocks = tempFileLocks;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        const string dbBackupFileName = "dbDump";

        if (!backupHandler.Configured)
        {
            logger.LogWarning("Backups are not configured, skipping creating one");
            return;
        }

        var backup = new Backup(Backup.CreateBackupName(backupHandler.UseXZCompression), -1);
        await database.Backups.AddAsync(backup, cancellationToken);

        var dbWrite = database.SaveChangesAsync(cancellationToken);

        var backupFolder = tempFileLocks.GetTempFilePath("backupWorkDir");
        using (await tempFileLocks.LockAsync(backupFolder, cancellationToken).ConfigureAwait(false))
        {
            string backupFile = Path.Combine(backupFolder, backup.Name);
            string databaseFile = Path.Combine(backupFolder, dbBackupFileName);

            await dbWrite;

            Directory.CreateDirectory(backupFolder);

            var start = DateTime.UtcNow;

            logger.LogTrace("Dumping database");
            await backupHandler.DumpDatabaseToFile(databaseFile, cancellationToken);

            // TODO: allow backup up with no redis required
            logger.LogTrace("Creating tar from db and redis");

            // Full path is not used here to make the created tar file work better
            await backupHandler.CreateBackupTarFile(backupFile, dbBackupFileName, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            backup.Size = new FileInfo(backupFile).Length;

            logger.LogInformation("Backup file creation took {Duration}", DateTime.UtcNow - start);

            await backupHandler.UploadBackup(backup, backupFile, cancellationToken);

            backup.Uploaded = true;

            // Delete the backup tar as the name is unique and they'll fill the disk otherwise
            File.Delete(backupFile);

            // TODO: should we delete backupFolder here?
        }

        logger.LogInformation("Created backup {Name} of size {Size}", backup.Name, backup.Size);

        // Don't want to fail to save the fact that we have uploaded it now
        // ReSharper disable once MethodSupportsCancellation
        await database.SaveChangesAsync();

        // Rely on next time when we make a backup to clear more than one backup
        if (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Not clearing old backups as job cancellation is requested");
            return;
        }

        try
        {
            await backupHandler.DeleteExcessBackups(database, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Deleting excess saves got canceled, not retrying it later, relying on a later backup job " +
                "cleaning things up");
        }
    }
}
