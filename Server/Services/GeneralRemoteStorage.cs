namespace ThriveDevCenter.Server.Services
{
    using Microsoft.Extensions.Configuration;

    public class GeneralRemoteStorage : BaseRemoteStorage
    {
        public GeneralRemoteStorage(IConfiguration configuration) : base(configuration["GeneralStorage:S3Region"],
            configuration["GeneralStorage:S3Endpoint"], configuration["GeneralStorage:S3AccessKey"],
            configuration["GeneralStorage:S3SecretKey"], configuration["GeneralStorage:S3Bucket"])
        {
        }
    }
}
