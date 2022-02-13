namespace ThriveDevCenter.Server.Services;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

public class BackupHandler
{
    private readonly ILogger<BackupHandler> logger;
    private readonly IConfiguration configuration;
    private readonly BackupStorage storage;

    private readonly int BackupsToKeep;
    private readonly string? RedisPath;

    public BackupHandler(ILogger<BackupHandler> logger, IConfiguration configuration, BackupStorage storage)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.storage = storage;

        Configured = false;

        if (!storage.Configured)
            return;

        if (!Convert.ToBoolean(configuration["Backup:Enabled"]))
            return;

        BackupsToKeep = Convert.ToInt32(configuration["Backup:BackupsToKeep"]);
        RedisPath = configuration["Backup:RedisPath"];

        if (string.IsNullOrEmpty(RedisPath) || BackupsToKeep < 1)
            return;

        logger.LogInformation("Backups are configured. Amount to keep: {BackupsToKeep}", BackupsToKeep);
        Configured = true;
    }

    public bool Configured { get; init; }

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

    protected void ThrowIfNotConfigured()
    {
        if (!Configured || RedisPath == null)
        {
            throw new InvalidOperationException("Backups are not configured");
        }
    }
}
