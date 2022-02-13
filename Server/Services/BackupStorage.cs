namespace ThriveDevCenter.Server.Services;

using Microsoft.Extensions.Configuration;

public class BackupStorage : BaseRemoteStorage
{
    public BackupStorage(IConfiguration configuration) : base(configuration["Backup:Storage:S3Region"],
        configuration["Backup:Storage:S3Endpoint"], configuration["Backup:Storage:S3AccessKey"],
        configuration["Backup:Storage:S3SecretKey"], configuration["Backup:Storage:S3Bucket"])
    {
    }
}
