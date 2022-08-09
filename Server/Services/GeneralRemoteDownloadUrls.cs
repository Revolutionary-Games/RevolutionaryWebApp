namespace ThriveDevCenter.Server.Services;

using System;
using Microsoft.Extensions.Configuration;
using Models;

public class GeneralRemoteDownloadUrls : BaseCDNDownload, IGeneralRemoteDownloadUrls
{
    public GeneralRemoteDownloadUrls(IConfiguration configuration) : base(
        configuration["GeneralStorage:Download:URL"],
        configuration["GeneralStorage:Download:Key"])
    {
    }

    public string CreateDownloadFor(StorageFile file, TimeSpan expiresIn)
    {
        return GenerateSignedURL(file.StoragePath, expiresIn);
    }
}

public interface IGeneralRemoteDownloadUrls : IBaseCDNDownload
{
    string CreateDownloadFor(StorageFile file, TimeSpan expiresIn);
}