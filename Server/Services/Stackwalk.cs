namespace ThriveDevCenter.Server.Services
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Configuration;
    using Shared.Converters;

    public class Stackwalk : IStackwalk
    {
        private const int PrimaryCallstackSubstituteCharacterCount = 400;
        private const int MaximumPrimaryCallstackLength = 15000;

        private static readonly Regex CrashedThreadRegex =
            new(@"Thread\s+\d+\s+\(crashed\).*", RegexOptions.IgnoreCase);

        private readonly HttpClient httpClient;
        private readonly Uri serviceBaseUrl;

        public Stackwalk(IConfiguration configuration)
        {
            var url = configuration["Crashes:StackwalkService"];

            if (string.IsNullOrEmpty(url))
                return;

            serviceBaseUrl = new Uri(url);

            httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            Configured = true;
        }

        public bool Configured { get; }

        public Task<string> PerformBlockingStackwalk(string dumpFilePath, CancellationToken cancellationToken)
        {
            return PerformBlockingStackwalk(File.OpenRead(dumpFilePath), cancellationToken);
        }

        public async Task<string> PerformBlockingStackwalk(Stream dumpContent, CancellationToken cancellationToken)
        {
            ThrowIfNotConfigured();

            var url = new Uri(serviceBaseUrl, "api/v1");

            using var form = new MultipartFormDataContent();

            // TODO: implement special mode for windows dumps that were compiled with mingw
            // form.Add(new StringContent("mingw"), "custom");

            form.Add(new StreamContent(dumpContent), "file", "file");
            var response = await httpClient.PostAsync(url, form, cancellationToken);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"Stackwalk service responded with unexpected status code: {response.StatusCode}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            await using var rawReader = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(rawReader, Encoding.UTF8);

            return await reader.ReadToEndAsync();
        }

        public string FindPrimaryCallstack(string decodedDump, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(decodedDump))
                return null;

            var builder = new StringBuilder(500);

            bool foundStart = false;

            foreach (var line in decodedDump.Split('\n'))
            {
                if (foundStart)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        break;

                    builder.Append(line);
                    builder.Append('\n');
                }
                else if (CrashedThreadRegex.IsMatch(line))
                {
                    foundStart = true;
                    builder.Append(line);
                    builder.Append('\n');
                }
            }

            if (!foundStart)
            {
                if (fallback)
                    return decodedDump.Truncate(PrimaryCallstackSubstituteCharacterCount);

                return null;
            }

            return builder.ToString().Truncate(MaximumPrimaryCallstackLength);
        }

        private void ThrowIfNotConfigured()
        {
            if (!Configured)
                throw new InvalidOperationException("Stackwalk service is not configured");
        }
    }

    public interface IStackwalk
    {
        bool Configured { get; }

        Task<string> PerformBlockingStackwalk(string dumpFilePath, CancellationToken cancellationToken);
        Task<string> PerformBlockingStackwalk(Stream dumpContent, CancellationToken cancellationToken);

        /// <summary>
        ///   Tries to find the primary (crashing thread) callstack from a stackwalk decoded crash dump
        /// </summary>
        /// <param name="decodedDump">
        ///   The stackwalk decoded output, for example from
        ///   <see cref="PerformBlockingStackwalk(string, CancellationToken)"/>
        /// </param>
        /// <param name="fallback">
        ///   If true then first few hundred characters are considered the primary callstack, if searching for it
        ///   failed
        /// </param>
        /// <returns>The found primary callstack or null</returns>
        string FindPrimaryCallstack(string decodedDump, bool fallback = true);
    }
}
