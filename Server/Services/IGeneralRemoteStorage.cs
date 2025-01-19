namespace RevolutionaryWebApp.Server.Services;

using System;
using Microsoft.Extensions.Configuration;

public interface IGeneralRemoteStorage : IBaseRemoteStorage
{
}

public class GeneralRemoteStorage : BaseRemoteStorage, IGeneralRemoteStorage
{
    public GeneralRemoteStorage(IConfiguration configuration) : base(configuration["GeneralStorage:S3Region"],
        configuration["GeneralStorage:S3Endpoint"], configuration["GeneralStorage:S3AccessKey"],
        configuration["GeneralStorage:S3SecretKey"], configuration["GeneralStorage:S3Bucket"],
        Convert.ToBoolean(configuration["GeneralStorage:VerifyChecksums"]))
    {
    }
}
