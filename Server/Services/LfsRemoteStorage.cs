namespace ThriveDevCenter.Server.Services
{
    using Microsoft.Extensions.Configuration;

    public class LfsRemoteStorage : BaseRemoteStorage
    {
        public LfsRemoteStorage(IConfiguration configuration) : base(configuration["Lfs:Storage:S3Region"],
            configuration["Lfs:Storage:S3Endpoint"], configuration["Lfs:Storage:S3AccessKey"],
            configuration["Lfs:Storage:S3SecretKey"], configuration["Lfs:Storage:S3Bucket"])
        {
        }
    }
}
