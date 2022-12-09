namespace ThriveDevCenter.Server.Services;

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public interface IFileDownloader
{
    /// <summary>
    ///   Downloads a file from URL to a local file
    /// </summary>
    /// <param name="url">Url to download</param>
    /// <param name="file">The local file to write to, will create the folder the file is in before downloading</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Task that finishes when the download is ready</returns>
    public Task DownloadFile(string url, string file, CancellationToken cancellationToken);
}

public class FileDownloader : IFileDownloader
{
    private readonly ILogger<FileDownloader> logger;
    private readonly HttpClient httpClient;

    public FileDownloader(ILogger<FileDownloader> logger, IHttpClientFactory httpClientFactory)
    {
        this.logger = logger;
        httpClient = httpClientFactory.CreateClient();
    }

    public async Task DownloadFile(string url, string file, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStreamAsync(cancellationToken);

        // Make sure the directory we want to write to exists
        Directory.CreateDirectory(Path.GetDirectoryName(file) ??
            throw new Exception("Failed to get parent folder for the file file to write download to"));

        try
        {
            await using var writer = File.OpenWrite(file);
            await content.CopyToAsync(writer, cancellationToken);
        }
        catch (OperationCanceledException e)
        {
            logger.LogWarning(e, "Write to download file canceled, attempting to delete temp file");
            File.Delete(file);
            throw;
        }
    }
}
