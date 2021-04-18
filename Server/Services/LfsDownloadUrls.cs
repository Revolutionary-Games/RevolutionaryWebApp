namespace ThriveDevCenter.Server.Services
{
    using System;
    using Microsoft.Extensions.Configuration;
    using Models;

    public class LfsDownloadUrls : BaseCDNDownload
    {
        public LfsDownloadUrls(IConfiguration configuration) : base(configuration["Lfs:Download:URL"],
            configuration["Lfs:Download:KEY"])
        {
        }

        public static string OidStoragePath(LfsProject project, string oid)
        {
            return $"{project.Slug}/objs/{oid[0..1]}/{oid[2..3]}/{oid[4..]}";
        }

        public string CreateDownloadFor(LfsObject lfsObject, TimeSpan expiresIn)
        {
            return GenerateSignedURL(lfsObject.StoragePath, expiresIn);
        }
    }
}
