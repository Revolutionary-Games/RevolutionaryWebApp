namespace RevolutionaryWebApp.Server.Services;

using Microsoft.Extensions.Configuration;

public interface ICLASignatureStorage : IBaseRemoteStorage
{
}

public class CLASignatureStorage : BaseRemoteStorage, ICLASignatureStorage
{
    public CLASignatureStorage(IConfiguration configuration) : base(configuration["CLA:Storage:S3Region"],
        configuration["CLA:Storage:S3Endpoint"], configuration["CLA:Storage:S3AccessKey"],
        configuration["CLA:Storage:S3SecretKey"], configuration["CLA:Storage:S3Bucket"])
    {
    }
}
