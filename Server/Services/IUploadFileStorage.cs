namespace RevolutionaryWebApp.Server.Services;

using Microsoft.Extensions.Configuration;

/// <summary>
///   Storage for users to upload files to (that aren't kept that long) for later processing
/// </summary>
public interface IUploadFileStorage : IBaseRemoteStorage
{
}

public class UploadFileStorage : BaseRemoteStorage, IUploadFileStorage
{
    public UploadFileStorage(IConfiguration configuration) : base(configuration["UploadStorage:S3Region"],
        configuration["UploadStorage:S3Endpoint"], configuration["UploadStorage:S3AccessKey"],
        configuration["UploadStorage:S3SecretKey"], configuration["UploadStorage:S3Bucket"])
    {
    }
}
