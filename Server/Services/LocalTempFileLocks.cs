namespace ThriveDevCenter.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;

    public class LocalTempFileLocks : ILocalTempFileLocks
    {
        private readonly string baseTempFilePath;
        private readonly Dictionary<string, SemaphoreSlim> requestedPaths = new();

        public LocalTempFileLocks(ILogger<LocalTempFileLocks> logger, IConfiguration configuration)
        {
            string path = configuration["TempFileStorage:Path"];

            if (string.IsNullOrEmpty(path))
            {
                path = "/tmp/ThriveDevCenter";
            }

            baseTempFilePath = Path.GetFullPath(path);

            Directory.CreateDirectory(baseTempFilePath);
            logger.LogInformation("Temporary files base path: {BaseTempFilePath}", baseTempFilePath);
        }

        public SemaphoreSlim GetTempFilePath(string suffix, out string path)
        {
            if (suffix.Length < 1 || suffix.StartsWith('/'))
                throw new ArgumentException("Path suffix is empty or starts with a slash");

            path = Path.Join(baseTempFilePath, suffix);

            lock (requestedPaths)
            {
                if (requestedPaths.TryGetValue(path, out SemaphoreSlim? existing))
                    return existing;

                var semaphore = new SemaphoreSlim(1, 1);
                requestedPaths[path] = semaphore;
                return semaphore;
            }
        }
    }

    public interface ILocalTempFileLocks
    {
        /// <summary>
        ///   Gets a temporary file path. Note that the receiver should lock the returned string while using the path
        ///   to avoid multiple places using the same temporary folder at once
        /// </summary>
        /// <param name="suffix">Suffix to add to the temporary folder</param>
        /// <param name="path">The final result path</param>
        /// <returns>A semaphore that needs to be locked while using the path</returns>
        SemaphoreSlim GetTempFilePath(string suffix, out string path);
    }
}
