namespace RevolutionaryWebApp.Server.Jobs.Maintenance;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;
using Models;
using Models.Pages;
using Pages;
using Services;
using Shared;
using Shared.Models.Enums;
using Shared.Models.Pages;
using YamlDotNet.Serialization;

/// <summary>
///   Imports non-existing posts from WordPress Markdown export, and related images (if missing based on name). This
///   eats output from https://github.com/lonekorean/wordpress-export-to-markdown
/// </summary>
[DisableConcurrentExecution(2000)]
public class ImportWordPressPosts : MaintenanceJobBase
{
    private const string PuImageMarker = "PU_IMAGE_FILE";

    private const string ImportBaseFolder = "posts";
    private const string ImportImagesBaseFolder = "posts/images";

    private readonly ILocalTempFileLocks tempFileLocks;
    private readonly IBackgroundJobClient jobClient;
    private readonly IUploadFileStorage fileStorage;

    private readonly Regex imageRegex = new(@"!\[.*?\]\(images\/(.*?)\)", RegexOptions.Compiled);

    public ImportWordPressPosts(ILogger<ImportWordPressPosts> logger,
        ApplicationDbContext operationDb, NotificationsEnabledDb operationStatusDb,
        ILocalTempFileLocks tempFileLocks, IBackgroundJobClient jobClient, IUploadFileStorage fileStorage) : base(
        logger, operationDb,
        operationStatusDb)
    {
        this.tempFileLocks = tempFileLocks;
        this.jobClient = jobClient;
        this.fileStorage = fileStorage;
    }

    protected override async Task RunOperation(ExecutedMaintenanceOperation operationData,
        CancellationToken cancellationToken)
    {
        var folder = tempFileLocks.GetTempFilePath("wordpress-import");

        using var folderLock = await tempFileLocks.LockAsync(folder, cancellationToken);

        var baseFolder = Path.Combine(folder, ImportBaseFolder);
        var imagesBaseFolder = Path.Combine(folder, ImportImagesBaseFolder);

        if (!Directory.Exists(baseFolder))
        {
            logger.LogWarning("Folder to import WordPress from doesn't exist at {BaseFolder}", baseFolder);
            operationData.Failed = true;
            operationData.ExtendedDescription = "Folder to import WordPress from doesn't exist";
            return;
        }

        var mediaFolder = await database.MediaFolders
            .Where(m => m.ParentFolderId == MediaFolder.WebsitePostsId && m.Name == "WordPress Import")
            .FirstOrDefaultAsync(cancellationToken);

        if (mediaFolder == null)
            throw new Exception("'WordPress Import' media folder not found");

        var randomizer = new Random();

        var yamlDeserializer = new DeserializerBuilder().Build();

        int imported = 0;
        int imageCount = 0;
        int errors = 0;

        foreach (var post in Directory.EnumerateFiles(baseFolder, "*.md", SearchOption.AllDirectories))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Post needs to have a two-part name, for example, "2019-10-12-progress-update-10-12-2019.md" starting
            // with the original publishing date and then the permalink.
            // So we split those out
            var parts = Path.GetFileNameWithoutExtension(post).Split('-', 4, StringSplitOptions.TrimEntries);

            if (parts.Length != 4)
            {
                logger.LogWarning("Invalid post name: {PostName}", post);
                ++errors;
                continue;
            }

            // Seconds are randomized so that in the case that there are multiple posts for each day, they get unique
            // publish times
            var date = new DateTime(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), 8, 0,
                (int)(randomizer.NextDouble() * 59), DateTimeKind.Utc);
            var permalink = parts[3];

            if (string.IsNullOrWhiteSpace(permalink))
            {
                logger.LogWarning("Invalid post permalink: {Permalink} ({PostName})", permalink, post);
                ++errors;
                continue;
            }

            // Can import this page, check if already imported
            if (await database.VersionedPages.AnyAsync(p => p.Permalink == permalink,
                    cancellationToken: cancellationToken))
            {
                logger.LogInformation("Post {Permalink} already exists, skipping", permalink);
                continue;
            }

            var rawText = await File.ReadAllTextAsync(post, cancellationToken);

            // Read the front matter of the Markdown
            if (!rawText.StartsWith("---"))
            {
                logger.LogWarning("Invalid post: {PostName} (doesn't start with front matter)", post);
                ++errors;
                continue;
            }

            int endOfFrontMatter = rawText.IndexOf("---", 10, StringComparison.Ordinal);

            var frontMatter = rawText.Substring(4, endOfFrontMatter - 4);

            var frontMatterData = yamlDeserializer.Deserialize<Dictionary<string, object?>>(frontMatter);

            frontMatterData.TryGetValue("title", out var rawTitle);

            var title = rawTitle?.ToString();

            if (string.IsNullOrWhiteSpace(title))
            {
                logger.LogWarning("Invalid post: {PostName} (no title)", post);
                ++errors;
                continue;
            }

            var editableContent = new StringBuilder(rawText.Substring(endOfFrontMatter + 4));

            imageCount += await ProcessContent(editableContent,
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), imagesBaseFolder, mediaFolder);

            if (cancellationToken.IsCancellationRequested)
                break;

            await SavePage(editableContent.ToString().Trim(), title, date, permalink);

            logger.LogInformation("Imported post {Permalink}", permalink);
            ++imported;
        }

        if (imported < 1 && errors > 0)
            operationData.Failed = true;

        if (errors > 0)
        {
            operationData.ExtendedDescription =
                $"Imported {imported} posts(s) with {imageCount} images(s). ERRORS: {errors}";
        }
        else
        {
            operationData.ExtendedDescription = $"Imported {imported} posts(s) with {imageCount} images(s)";
        }
    }

    private async Task<int> ProcessContent(StringBuilder editableContent, string postDate, string localBase,
        MediaFolder parentFolder)
    {
        var imageCount = 0;

        var fullText = editableContent.ToString();

        // Detect images that should be imported
        var matches = imageRegex.Matches(fullText);

        foreach (Match match in matches)
        {
            // Detect the image name and check if it exists already
            var imageName = match.Groups[1].Value;

            var databaseImage =
                await database.MediaFiles.FirstOrDefaultAsync(m =>
                    m.FolderId == parentFolder.Id && m.Name == imageName);

            // If it doesn't, we need to upload it
            if (databaseImage == null)
            {
                databaseImage = await HandleImageImport(editableContent, postDate, localBase, parentFolder, imageName,
                    match);

                // If null, it was a banner already replaced in the text
                if (databaseImage == null)
                    continue;
            }

            var typeRaw = Path.GetExtension(imageName);
            var type = typeRaw.Substring(1);

            // And then finally we will replace the link with the image
            editableContent.Replace($"images/{imageName}", $"media:{type}:{databaseImage.GlobalId}");
            ++imageCount;
        }

        // Detect other changes we may want to make, like YouTube embedding
        if (fullText.Contains("youtube"))
        {
            Debugger.Break();
        }

        return imageCount;
    }

    private async Task<MediaFile?> HandleImageImport(StringBuilder editableContent, string postDate, string localBase,
        MediaFolder parentFolder, string imageName, Match match)
    {
        var sourceFile = Path.Join(localBase, imageName);

        // Check if the source file is a PU banner, if so, we can replace it with a simple bbcode.
        // Reading the whole file into memory is not very efficient here, but it doesn't really matter in this import
        // code that is only ever getting used once.
        if (File.Exists(sourceFile) && (await File.ReadAllTextAsync(sourceFile)).StartsWith(PuImageMarker))
        {
            logger.LogInformation("Image {ImageName} is a PU banner, replacing with bbcode", imageName);

            editableContent.Replace(match.Groups[0].Value, $"[puImage]{postDate}[/puImage]");
            return null;
        }

        logger.LogInformation("Image {ImageName} not found, importing it", imageName);

        var databaseImage = new MediaFile(imageName, Guid.NewGuid(), parentFolder.Id, null)
        {
            MetadataVisibility = GroupType.Developer,
            ModifyAccess = GroupType.SitePagePublisher,
            OriginalFileSize = new FileInfo(sourceFile).Length,
        };

        if (await database.MediaFiles.AnyAsync(m => m.GlobalId == databaseImage.GlobalId))
            throw new Exception("Conflicting UUID, please retry");

        // Open a stream here to make sure the file is readable
        await using var uploadStream = File.OpenRead(sourceFile);

        await database.MediaFiles.AddAsync(databaseImage);

        // We have to save the image here to know for sure what the intermediate processing path will be (as it uses
        // the image ID)
        await database.SaveChangesAsync();

        var savePath = databaseImage.GetIntermediateProcessingPath();

        // Queue a job to make sure failed uploads are deleted
        jobClient.Schedule<DeleteUploadStorageFileJob>(x => x.Execute(savePath,
                DeleteUploadStorageFileJob.RelatedRecordType.None, 0, CancellationToken.None),
            AppInfo.RemoteStorageUploadExpireTime * 4);
        try
        {
            await fileStorage.UploadFile(savePath, uploadStream, MimeTypes.GetMimeType(sourceFile), true,
                CancellationToken.None);

            var uploadedSize = await fileStorage.GetObjectSize(savePath);

            if (uploadedSize != new FileInfo(sourceFile).Length)
            {
                await fileStorage.DeleteObject(savePath);
                throw new Exception("Size mismatch on image upload");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to upload image {ImageName}, removing from DB", imageName);

            // Don't keep around in the database
            database.MediaFiles.Remove(databaseImage);
            await database.SaveChangesAsync();
            throw;
        }

        // Now that it is uploaded, we can trigger a background job to handle the upload
        jobClient.Enqueue<ProcessUploadedImageJob>(x =>
            x.Execute(databaseImage.Id, savePath, CancellationToken.None));

        return databaseImage;
    }

    private async Task SavePage(string content, string title, DateTime date, string permalink)
    {
        var page = new VersionedPage(title)
        {
            LastEditComment = "Imported from WordPress",
            LatestContent = content,
            Permalink = permalink,
            Visibility = PageVisibility.Public,
            Type = PageType.Post,
            PublishedAt = date,
        };

        // TODO: enable
        logger.LogInformation("TODO: enable actually saving");

        // await database.VersionedPages.AddAsync(page);

        // We must have the ID here, so I guess we are saving database changes now
        await database.SaveChangesAsync();

        return;

        // We need to run some post-import actions. So queue those long into the future to make sure they will run
        jobClient.Schedule<UpdatePageUsedMediaJob>(x => x.Execute(page.Id, CancellationToken.None),
            TimeSpan.FromMinutes(30));

        // Probably not super important to clear the news page so that is left out
    }
}
