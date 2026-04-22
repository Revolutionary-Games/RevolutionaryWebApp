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

    private enum CacheKind
    {
        Filesystem,
        FilesystemFile,
        PodmanImage,
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

        // Run a periodic full prune every 3 months regardless of requested preserve size
        var now = DateTime.UtcNow;
        var mustDoFullPrune = cacheInfo.LastFullPruneRun == default ||
            now - cacheInfo.LastFullPruneRun >= TimeSpan.FromDays(90);

        if (mustDoFullPrune)
        {
            logger.LogInformation("Triggering a scheduled full prune (last run: {LastRun})",
                cacheInfo.LastFullPruneRun == default ? "never" : cacheInfo.LastFullPruneRun);
            cachePreserveSize = 0; // behave as a full prune request
        }

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
            // Smart pruning algorithm
            // 1) Identify filesystem cache items by known structure
            // 2) Identify podman images we manage
            // 3) Force-delete anything older than the retention window (6 months)
            // 4) From the rest, keep newest-first until under cachePreserveSize, with preference:
            //    - newest image per tag
            //    - names containing master/main treated as slightly newer

            const int retentionDays = 180;
            var retentionThreshold = now - TimeSpan.FromDays(retentionDays);

            // Build filesystem cache items
            var fsItems = new List<CacheEntry>();

            void AddLeafDirectoriesIfExist(string path)
            {
                if (!Directory.Exists(path))
                    return;

                foreach (var leaf in Directory.EnumerateDirectories(path))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(leaf);
                        var created = dirInfo.CreationTimeUtc;
                        var size = GetDirectorySizeSafe(leaf, cancellationToken);

                        fsItems.Add(new CacheEntry
                        {
                            Kind = CacheKind.Filesystem,
                            Identifier = leaf,
                            Size = size,
                            CreatedUtc = created,
                            PriorityBoost = NameIndicatesMain(dirInfo.Name) ? TimeSpan.FromDays(7) : TimeSpan.Zero,
                        });
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Failed to stat cache directory: {Dir}", leaf);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            // Known structures
            var execRoot = Path.Combine(baseCacheFolder, "executor");
            AddLeafDirectoriesIfExist(Path.Combine(execRoot, "safe", "named"));
            AddLeafDirectoriesIfExist(Path.Combine(execRoot, "safe", "shared"));
            AddLeafDirectoriesIfExist(Path.Combine(execRoot, "unsafe", "named"));
            AddLeafDirectoriesIfExist(Path.Combine(execRoot, "unsafe", "shared"));

            // Also consider any top-level directories that are not part of the known structure
            foreach (var top in Directory.EnumerateDirectories(baseCacheFolder))
            {
                // Skip the executor tree; already handled above
                if (Path.GetFileName(top).Equals("executor", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var dirInfo = new DirectoryInfo(top);
                    var created = dirInfo.CreationTimeUtc;
                    var size = GetDirectorySizeSafe(top, cancellationToken);

                    fsItems.Add(new CacheEntry
                    {
                        Kind = CacheKind.Filesystem,
                        Identifier = top,
                        Size = size,
                        CreatedUtc = created,
                        PriorityBoost = NameIndicatesMain(dirInfo.Name) ? TimeSpan.FromDays(7) : TimeSpan.Zero,
                    });
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Failed to stat top-level cache directory: {Dir}", top);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            // Also consider loose files under base (excluding our JSON)
            foreach (var file in Directory.EnumerateFiles(baseCacheFolder, "*", SearchOption.TopDirectoryOnly))
            {
                if (file.EndsWith(UsedImagesInfoFile))
                    continue;

                try
                {
                    var info = new FileInfo(file);
                    fsItems.Add(new CacheEntry
                    {
                        Kind = CacheKind.FilesystemFile,
                        Identifier = file,
                        Size = info.Length,
                        CreatedUtc = info.CreationTimeUtc,
                    });
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Failed to stat cache file: {File}", file);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            // Build image items; determine newest-per-tag
            var imageItems = new List<CacheEntry>();
            var newestByRepo = images
                .SelectMany(img => img.Names.Select(n => new { RepoTag = n, Img = img }))
                .GroupBy(x => ExtractRepo(x.RepoTag))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => GetImageCreatedUtc(x.Img)).First().Img);

            foreach (var image in images)
            {
                var anyName = image.Names.FirstOrDefault() ?? "<unnamed>";
                var repo = ExtractRepo(anyName);
                var isNewestOfRepo = newestByRepo.TryGetValue(repo, out var newest) && ReferenceEquals(newest, image);

                imageItems.Add(new CacheEntry
                {
                    Kind = CacheKind.PodmanImage,
                    Identifier = anyName,
                    Size = image.Size,
                    CreatedUtc = GetImageCreatedUtc(image),
                    PriorityBoost = isNewestOfRepo ? TimeSpan.FromDays(7) : TimeSpan.Zero,
                    PodmanImageName = anyName,
                });

                cancellationToken.ThrowIfCancellationRequested();
            }

            // Phase 1: enforce 6-month retention (delete old items regardless of size)
            foreach (var entry in fsItems.Where(i => i.CreatedUtc < retentionThreshold))
            {
                TryDeleteFilesystemEntry(entry);
                cancellationToken.ThrowIfCancellationRequested();
            }

            foreach (var entry in imageItems.Where(i => i.CreatedUtc < retentionThreshold))
            {
                await TryDeletePodman(entry, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }

            // Recompute remaining items and total size
            fsItems = fsItems.Where(ExistsFilesystemEntry).ToList();
            imageItems = (await GetPodmanImagesWeManage(cancellationToken))
                .Select(img =>
                {
                    var anyName = img.Names.FirstOrDefault() ?? "<unnamed>";
                    var repo = ExtractRepo(anyName);
                    var isNewest = images
                        .SelectMany(im => im.Names.Select(n => new { RepoTag = n, Img = im }))
                        .GroupBy(x => ExtractRepo(x.RepoTag))
                        .ToDictionary(g => g.Key, g => g.OrderByDescending(x => GetImageCreatedUtc(x.Img)).First().Img)
                        .TryGetValue(repo, out var newest) && ReferenceEquals(newest, img);

                    return new CacheEntry
                    {
                        Kind = CacheKind.PodmanImage,
                        Identifier = anyName,
                        Size = img.Size,
                        CreatedUtc = GetImageCreatedUtc(img),
                        PriorityBoost = isNewest ? TimeSpan.FromDays(7) : TimeSpan.Zero,
                        PodmanImageName = anyName,
                    };
                })
                .ToList();

            long totalRemaining = fsItems.Sum(i => i.Size) + imageItems.Sum(i => i.Size);
            logger.LogInformation("Total remaining cache items size before size-based pruning: {SizeGiB} GiB",
                Math.Round(totalRemaining / (double)GlobalConstants.GIBIBYTE, 2));

            if (totalRemaining > cachePreserveSize)
            {
                // Phase 2: delete oldest-first until under preserve size
                var combined = fsItems.Concat(imageItems).ToList();
                var ordered = combined
                    .OrderByDescending(i => i.CreatedUtc + i.PriorityBoost) // newer first
                    .ToList();

                long kept = 0;
                var keepSet = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in ordered)
                {
                    if (kept + item.Size <= cachePreserveSize)
                    {
                        kept += item.Size;
                        keepSet.Add(item.Identifier);
                    }
                }

                logger.LogInformation("Keeping {Count} items with total size {SizeGiB} GiB; pruning the rest",
                    keepSet.Count, Math.Round(kept / (double)GlobalConstants.GIBIBYTE, 2));

                foreach (var item in combined.Where(i => !keepSet.Contains(i.Identifier)))
                {
                    if (item.Kind == CacheKind.PodmanImage)
                    {
                        await TryDeletePodman(item, cancellationToken);
                    }
                    else
                    {
                        TryDeleteFilesystemEntry(item);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }

        // If we executed a full prune, record its time
        if (cachePreserveSize <= 0)
        {
            cacheInfo.LastFullPruneRun = now;
            savedInfo = false;
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

        logger.LogInformation("Added podman image {Name} to used images, now {Count} in total", name,
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

    private static bool NameIndicatesMain(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("master") || lower.Contains("main");
    }

    private static string ExtractRepo(string repoTag)
    {
        // repo[:tag] → repo
        var idx = repoTag.IndexOf(':');
        return idx >= 0 ? repoTag[..idx] : repoTag;
    }

    private static long GetDirectorySizeSafe(string path, CancellationToken ct)
    {
        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // ignore broken files
                }

                ct.ThrowIfCancellationRequested();
            }

            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static bool ExistsFilesystemEntry(CacheEntry entry)
    {
        if (entry.Kind == CacheKind.Filesystem)
        {
            return Directory.Exists(entry.Identifier);
        }

        if (entry.Kind == CacheKind.FilesystemFile)
            return File.Exists(entry.Identifier);

        return false;
    }

    private static DateTime GetImageCreatedUtc(PodmanImageInfo image)
    {
        // Order of preference:
        // 1) Parse CreatedTime/CreatedAt raw strings if present
        // 2) Use numeric Created as Unix epoch (auto-detect seconds vs milliseconds)
        // 3) Fallback to "now"

        static bool TryParsePodmanTime(string? raw, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            // Common forms seen from podman:
            // - 2025-11-28T19:46:14Z
            // - 2025-11-27 16:49:00 +0000 UTC
            // Normalize a few variants before parsing.
            var s = raw.Trim();
            try
            {
                // Fast path: ISO 8601 Z/offset
                if (DateTimeOffset.TryParse(s, out var dto))
                {
                    utc = dto.UtcDateTime;
                    return true;
                }

                // Replace trailing <c> UTC</c> marker and fix "+0000" into "+00:00"
                if (s.EndsWith(" UTC", StringComparison.OrdinalIgnoreCase))
                    s = s[..^4];

                // If offset has form "+HHMM" insert colon to make it RFC3339-compliant
                // Find last space; expect datetime then space "+HHMM"/"-HHMM"
                var lastSpace = s.LastIndexOf(' ');
                if (lastSpace > 0 && lastSpace < s.Length - 1)
                {
                    var tail = s[(lastSpace + 1)..];
                    if (tail.Length == 5 && (tail[0] == '+' || tail[0] == '-') && char.IsDigit(tail[1]))
                    {
                        s = s[..(lastSpace + 1)] + tail[..3] + ":" + tail[3..];
                    }
                }

                if (DateTimeOffset.TryParse(s, out dto))
                {
                    utc = dto.UtcDateTime;
                    return true;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        if (TryParsePodmanTime(image.CreatedTimeRaw, out var parsed))
            return parsed;
        if (TryParsePodmanTime(image.CreatedAtRaw, out parsed))
            return parsed;

        if (image.Created > 0)
        {
            try
            {
                // Heuristic: values > 1e12 are likely milliseconds
                var dto = image.Created > 1_000_000_000_000 ?
                    DateTimeOffset.FromUnixTimeMilliseconds(image.Created) :
                    DateTimeOffset.FromUnixTimeSeconds(image.Created);
                return dto.UtcDateTime;
            }
            catch
            {
                // ignore and fallback
            }
        }

        return DateTime.UtcNow;
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

    private async Task TryDeletePodman(CacheEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrEmpty(entry.PodmanImageName))
            {
                logger.LogInformation("Deleting podman image: {Name}", entry.PodmanImageName);
                await DeletePodmanImage(entry.PodmanImageName, cancellationToken);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to delete podman image: {Name}", entry.PodmanImageName);
        }
    }

    private void TryDeleteFilesystemEntry(CacheEntry entry)
    {
        try
        {
            if (entry.Kind == CacheKind.Filesystem)
            {
                // These should all be just the top level folders in the cache, so we don't need to go up from these
                var target = entry.Identifier;
                if (Directory.Exists(target))
                {
                    logger.LogInformation("Deleting cache directory: {Dir}", target);
                    Directory.Delete(target, true);
                }
            }
            else if (entry.Kind == CacheKind.FilesystemFile)
            {
                if (File.Exists(entry.Identifier))
                {
                    logger.LogInformation("Deleting cache file: {File}", entry.Identifier);
                    File.Delete(entry.Identifier);
                }
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to delete cache entry: {Id}", entry.Identifier);
        }
    }

    private class CacheInfo
    {
        public HashSet<string> UsedImages { get; set; } = new();

        public DateTime LastFullPruneRun { get; set; }
    }

    private sealed class CacheEntry
    {
        public CacheKind Kind { get; init; }

        /// <summary>
        ///   Path for filesystem, name for image
        /// </summary>
        public string Identifier { get; init; } = string.Empty;

        public long Size { get; init; }
        public DateTime CreatedUtc { get; init; }
        public TimeSpan PriorityBoost { get; init; }
        public string? PodmanImageName { get; init; }
    }
}
