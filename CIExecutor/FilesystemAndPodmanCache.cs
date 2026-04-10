namespace CIExecutor;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RevolutionaryWebApp.Server.Common.Services;
using SharedBase.Utilities;

public sealed class FilesystemAndPodmanCache : IExecutorCache, IDisposable
{
    private const string UsedImagesInfoFile = "executor-cache-info.json";

    private readonly ILogger logger;
    private readonly string baseCacheFolder;

    private CacheInfo cacheInfo = new();

    private bool savedInfo;

    public FilesystemAndPodmanCache(ILogger logger, string baseCacheFolder)
    {
        this.logger = logger;
        this.baseCacheFolder = baseCacheFolder;

        Directory.CreateDirectory(baseCacheFolder);
        logger.LogInformation("Cache starting with base folder: {Folder}", baseCacheFolder);
        LoadUsedPodmanImages().Wait(TimeSpan.FromSeconds(90));
    }

    public string BaseFolder => baseCacheFolder;

    public async Task<long> CalculateCacheSizeAsync(CancellationToken cancellationToken)
    {
        var podmanSize = CalculateBytesUsedByPodmanImages(cancellationToken);

        var fileSizes = Directory.EnumerateFiles(baseCacheFolder, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);

        logger.LogInformation("Found files in cache with a total size of {Size} GiB",
            fileSizes / (double)GlobalConstants.GIBIBYTE);

        var podman = await podmanSize;

        return podman + fileSizes;
    }

    public async Task<long> PruneCacheAsync(long cachePreserveSize, CancellationToken cancellationToken)
    {
        var images = await GetPodmanImagesWeManage(cancellationToken);

        if (cachePreserveSize <= 0)
        {
            // Delete everything!
            logger.LogInformation("Deleting everything in cache and in podman images");

            bool errors = false;

            foreach (var podmanImageInfo in images)
            {
                try
                {
                    await DeletePodmanImage(podmanImageInfo.Names.First(), cancellationToken);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to delete image: {Name}", podmanImageInfo.Names.First());
                    errors = true;
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!errors)
            {
                logger.LogInformation("Will forget what podman images have been used");
                cacheInfo.UsedImages.Clear();
                savedInfo = false;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(baseCacheFolder, "*"))
            {
                if (Directory.Exists(entry))
                {
                    // Should be a directory
                    try
                    {
                        Directory.Delete(entry, true);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Failed to delete folder: {Folder}", entry);
                    }
                }
                else
                {
                    // A file
                    // Do not delete our data file
                    if (entry.EndsWith(UsedImagesInfoFile))
                        continue;

                    try
                    {
                        File.Delete(entry);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Failed to delete file: {File}", entry);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        else
        {
            // TODO: a smart algorithm

            // TODO: leave the latest image of each name if enough space is available

            // Then leave folders that contain "master" in the name, up to the file limit

            throw new NotImplementedException();
        }

        // Looks like we don't really have an efficient way to get the new cache size, so recalculate
        return await CalculateCacheSizeAsync(cancellationToken);
    }

    public Task NotifyUsedPodmanImage(string name)
    {
        var count = cacheInfo.UsedImages.Count;

        cacheInfo.UsedImages.Add(name);
        if (cacheInfo.UsedImages.Count == count)
            return Task.CompletedTask;

        logger.LogInformation("Added podman image {Name} to used images, now {Count}", name,
            cacheInfo.UsedImages.Count);
        savedInfo = false;

        // TODO: we could in theory write immediately, but instead we just save when quitting

        return Task.CompletedTask;
    }

    public async Task LoadUsedPodmanImages()
    {
        var file = Path.Join(baseCacheFolder, UsedImagesInfoFile);

        if (!File.Exists(file))
            return;

        try
        {
            await using var reader = File.OpenRead(file);
            var decoded = JsonSerializer.Deserialize<CacheInfo>(reader) ?? throw new NullDecodedJsonException();
            cacheInfo = decoded;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to load used podman images");
        }

        logger.LogInformation("Loaded {Count} used podman images", cacheInfo.UsedImages.Count);
        savedInfo = true;
    }

    public async Task SaveUsedPodmanImages()
    {
        var file = Path.Join(baseCacheFolder, UsedImagesInfoFile);
        await using var writer = File.Create(file);
        await JsonSerializer.SerializeAsync(writer, cacheInfo);

        savedInfo = true;
    }

    public void Dispose()
    {
        if (!savedInfo)
            SaveUsedPodmanImages().Wait(TimeSpan.FromSeconds(90));
    }

    private async Task<long> CalculateBytesUsedByPodmanImages(CancellationToken cancellationToken)
    {
        var images = await GetPodmanImagesWeManage(cancellationToken);

        if (images.Count == 0)
        {
            logger.LogInformation("Found no images in podman that we manage");
            return 0;
        }

        var size = images.Sum(image => image.Size);
        logger.LogInformation("Found {Count} images in podman we manage with a total size of {Size} GiB", images.Count,
            Math.Round(size / (double)GlobalConstants.GIBIBYTE, 2));

        return size;
    }

    private async Task<List<PodmanImageInfo>> GetPodmanImagesWeManage(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("podman", "images --format json")
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("Failed to start podman images command");

        using var reader = process.StandardOutput;
        var json = await reader.ReadToEndAsync(cancellationToken);
        var images = JsonSerializer.Deserialize<List<PodmanImageInfo>>(json) ?? throw new NullDecodedJsonException();

        var used = cacheInfo.UsedImages;

        // Filter out images to only ones we manage
        images = images.Where(image => image.Names.Any(name => used.Any(name.EndsWith))).ToList();
        return images;
    }

    private async Task DeletePodmanImage(string name, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("podman", ["rmi", name])
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("Failed to start podman images command");

        using var reader = process.StandardOutput;
        var data = await reader.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new Exception($"Failed to delete podman image: {data}");
    }

    private class CacheInfo
    {
        public HashSet<string> UsedImages { get; set; } = new();

        // TODO: a field for last done full clean and then do a full clean at some interval
    }
}
