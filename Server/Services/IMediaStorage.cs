namespace RevolutionaryWebApp.Server.Services;

using System;
using Microsoft.Extensions.Configuration;

/// <summary>
///   Storage that has public access to it. For use with public media to be shown on pages.
/// </summary>
public interface IMediaStorage : IBaseRemoteStorage
{
}

public class MediaStorage : BaseRemoteStorage, IMediaStorage
{
    public MediaStorage(IConfiguration configuration) : base(configuration["MediaStorage:S3Region"],
        configuration["MediaStorage:S3Endpoint"], configuration["MediaStorage:S3AccessKey"],
        configuration["MediaStorage:S3SecretKey"], configuration["MediaStorage:S3Bucket"],
        Convert.ToBoolean(configuration["MediaStorage:VerifyChecksums"]))
    {
    }
}
