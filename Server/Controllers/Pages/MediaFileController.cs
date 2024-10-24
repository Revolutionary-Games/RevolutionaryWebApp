namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Text;
using Authorization;
using DevCenterCommunication.Models;
using Hangfire;
using Jobs.Pages;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Pages;
using Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Models.Pages;
using SharedBase.Utilities;
using SixLabors.ImageSharp;
using Path = System.IO.Path;

/// <summary>
///   Handles individual media files (as opposed to folders and their items that <see cref="MediaFolderController"/>
///   handles)
/// </summary>
[ApiController]
public class MediaFileController : Controller
{
    private const string MediaUploadProtectionPurposeString = "MediaFileController.Upload.v1";

    private readonly ILogger<MediaFileController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IUploadFileStorage fileStorage;
    private readonly IBackgroundJobClient jobClient;
    private readonly ITimeLimitedDataProtector dataProtector;

    public MediaFileController(ILogger<MediaFileController> logger, NotificationsEnabledDb database,
        IUploadFileStorage fileStorage, IDataProtectionProvider dataProtectionProvider, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.fileStorage = fileStorage;
        this.jobClient = jobClient;

        dataProtector = dataProtectionProvider.CreateProtector(MediaUploadProtectionPurposeString)
            .ToTimeLimitedDataProtector();
    }

    [HttpGet("{id:long}")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.User)]
    public async Task<ActionResult<MediaFileDTO>> SingleFile([Required] long id)
    {
        var mediaFile = await database.MediaFiles.FindAsync(id);

        if (mediaFile == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();
        var groups = user.AccessCachedGroupsOrThrow();

        if (user.Id != mediaFile.UploadedById && user.Id != mediaFile.LastModifiedById &&
            !groups.HasGroup(mediaFile.MetadataVisibility) && !groups.HasGroup(mediaFile.ModifyAccess))
        {
            // Can't see this item
            return NotFound();
        }

        return mediaFile.GetDTO();
    }

    [HttpPost("startUpload")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.User)]
    public async Task<ActionResult<UploadRequestResponse>> StartUpload(
        [Required] [FromBody] UploadMediaFileRequestForm request)
    {
        if (request.ModifyAccess == GroupType.NotLoggedIn || request.MetadataVisibility == GroupType.NotLoggedIn)
            return BadRequest("Access to not logged in is not allowed (currently)");

        // Make sure name doesn't have weird whitespace
        request.Name = request.Name.Trim();

        var type = Path.GetExtension(request.Name);

        if (!AppInfo.MediaFileTypes.Contains(type))
            return BadRequest("File type is not supported (as a media file)");

        if (await database.MediaFiles.AnyAsync(m => m.GlobalId == request.MediaFileId))
            return BadRequest("Conflicting UUID, please retry");

        var folder = await database.MediaFolders.FindAsync(request.Folder);

        if (folder == null)
            return BadRequest("Specified parent folder doesn't exist (or you don't have access to it)");

        var user = HttpContext.AuthenticatedUserOrThrow();
        var groups = user.AccessCachedGroupsOrThrow();

        // Check for no write access
        if (user.Id != folder.LastModifiedById && user.Id != folder.OwnedById &&
            !groups.HasGroup(folder.ContentWriteAccess))
        {
            // If user is allowed info about the folder we can at least show a more specific error
            if (groups.HasGroup(folder.ContentReadAccess) || groups.HasGroup(folder.FolderModifyAccess))
            {
                return BadRequest("You do not have write access to the specified media folder");
            }

            return BadRequest("Specified parent folder doesn't exist (or you don't have access to it)");
        }

        if (await database.MediaFiles.AnyAsync(m => m.FolderId == folder.Id && m.Name == request.Name))
            return BadRequest("Name is already in use in this folder, please pick a different name");

        var file = new MediaFile(request.Name, request.MediaFileId, folder.Id, user.Id)
        {
            OriginalFileSize = request.Size,
            MetadataVisibility = request.MetadataVisibility,
            ModifyAccess = request.ModifyAccess,
        };

        var storagePath = file.GetUploadPath();

        var uploadURL = fileStorage.CreatePresignedUploadURL(storagePath, AppInfo.RemoteStorageUploadExpireTime);

        await database.MediaFiles.AddAsync(file);
        await database.SaveChangesAsync();

        var token = new UploadVerifyToken
        {
            TargetItem = file.Id,
        };

        var tokenStr = JsonSerializer.Serialize(token);

        // Queue a job to make sure failed uploads are deleted
        jobClient.Schedule<DeleteUploadStorageFileJob>(x => x.Execute(storagePath,
                DeleteUploadStorageFileJob.RelatedRecordType.MediaFile, file.Id,
                CancellationToken.None),
            AppInfo.RemoteStorageUploadExpireTime * 2);

        return new UploadRequestResponse
        {
            UploadUrl = uploadURL,
            VerifyToken = dataProtector.Protect(tokenStr, AppInfo.RemoteStorageUploadExpireTime),
        };
    }

    [HttpPost("finishUpload")]
    public async Task<ActionResult> ReportFinishedUpload([Required] [FromBody] TokenForm tokenForm)
    {
        // Verify token first as there is no other protection on this endpoint
        UploadVerifyToken verifiedToken;

        try
        {
            verifiedToken =
                JsonSerializer.Deserialize<UploadVerifyToken>(dataProtector.Unprotect(tokenForm.Token)) ??
                throw new NullDecodedJsonException();
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to verify media file upload token: {@E}", e);
            return BadRequest("Invalid upload token");
        }

        var mediaFile = await database.MediaFiles.FindAsync(verifiedToken.TargetItem);

        if (mediaFile == null)
            return BadRequest("Invalid specified file in verify token");

        if (mediaFile.Deleted || mediaFile.Processed)
            return BadRequest("Target media file state is incorrect");

        var uploadPath = mediaFile.GetUploadPath();
        var processingPath = mediaFile.GetIntermediateProcessingPath();

        // Verify that the file is properly in remote storage and copy it to an unmodifiable path
        try
        {
            var actualSize = await fileStorage.GetObjectSize(uploadPath);

            if (actualSize != mediaFile.OriginalFileSize)
            {
                // Try to delete the file to not leave it hanging around
                logger.LogWarning("Detected partial upload to remote media storage, trying to delete partial upload");
                await fileStorage.DeleteObject(uploadPath);
                logger.LogInformation("Partial medial upload deleted");
                return BadRequest("The upload was only partially successful. Remote storage file size doesn't " +
                    "match the expected value.");
            }
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to check object size in storage: {@E}", e);
            return BadRequest("Failed to retrieve the object size");
        }

        try
        {
            // Move the uploaded file to a path the user can't anymore access to overwrite it
            await fileStorage.MoveObject(uploadPath, processingPath);

            // Check the stored file size once again
            var movedSize = await fileStorage.GetObjectSize(processingPath);

            if (movedSize != mediaFile.OriginalFileSize)
            {
                logger.LogWarning("Media file size after move doesn't match anymore");

                logger.LogInformation("Attempting to delete the copied invalid file");

                await fileStorage.DeleteObject(processingPath);

                return BadRequest("Error copying the uploaded file in remote storage (size changed)");
            }
        }
        catch (Exception e)
        {
            logger.LogError("Move to right path remote media storage file failed: {@E}", e);
            return BadRequest("Error copying the uploaded file in remote storage");
        }

        // Test that the image can be decoded properly before reporting success to be pretty sure that post-processing
        // of the file succeeds
        // TODO: if we allow stuff like short videos in the future the following check needs to be extended
        try
        {
            await using var readStream = await fileStorage.GetObjectContent(processingPath);

            // TODO: does this need to read the full data? Or is a basic data check enough.
            // using var image = await Image.LoadAsync(readStream);

            var info = await Image.IdentifyAsync(readStream);
            if (info.Size.Height > AppInfo.MaxMediaImageDimension || info.Size.Width > AppInfo.MaxMediaImageDimension)
            {
                await fileStorage.DeleteObject(processingPath);
                return BadRequest("Maximum image file dimensions are " +
                    $"{AppInfo.MaxMediaImageDimension}x{AppInfo.MaxMediaImageDimension}");
            }
        }
        catch (Exception e)
        {
            logger.LogError("Loading uploaded media file as an image failed: {@E}", e);
            await fileStorage.DeleteObject(processingPath);
            return BadRequest("The uploaded file doesn't appear to be a valid image file");
        }

        // Then update the model state and save changes
        mediaFile.BumpUpdatedAt();
        await database.SaveChangesAsync();

        // Queue a job to process the file to the final form and make it ready to use
        jobClient.Enqueue<ProcessUploadedImageJob>(x =>
            x.Execute(mediaFile.Id, processingPath, CancellationToken.None));

        logger.LogInformation("MediaFile {Id} is now uploaded and will be processed next", mediaFile.Id);

        // The user uploading the file, will see the success but the processing likely won't be done yet
        return Ok();
    }

    [HttpDelete("{id:long}")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.User)]
    public async Task<IActionResult> MarkDeleted([Required] long id)
    {
        var mediaFile = await database.MediaFiles.FindAsync(id);

        if (mediaFile == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (!CanModify(mediaFile, user))
            return NotFound("File does not exist or you don't have write access to it");

        if (mediaFile.Deleted)
        {
            return Ok("File already marked deleted");
        }

        mediaFile.Deleted = true;
        mediaFile.BumpUpdatedAt();

        await database.ActionLogEntries.AddAsync(new ActionLogEntry(
            $"Media file {mediaFile.Name.Truncate()} ({mediaFile.Id}) marked deleted in " +
            $"folder {mediaFile.FolderId}")
        {
            PerformedById = user.Id,
            Extended = mediaFile.Name,
        });

        logger.LogInformation("MediaFile {Name} ({Id}) marked as deleted by {Email}", mediaFile.Name, mediaFile.Id,
            user.Email);

        await database.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("{id:long}/restore")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.User)]
    public async Task<IActionResult> RestoreDeleted([Required] long id)
    {
        var mediaFile = await database.MediaFiles.FindAsync(id);

        if (mediaFile == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (!CanModify(mediaFile, user))
            return NotFound("File does not exist or you don't have write access to it");

        if (!mediaFile.Deleted)
        {
            return BadRequest("Cannot restore a file not in soft-deleted state");
        }

        mediaFile.Deleted = false;
        mediaFile.BumpUpdatedAt();

        await database.ActionLogEntries.AddAsync(new ActionLogEntry(
            $"Media file {mediaFile.Name.Truncate()} ({mediaFile.Id}) restored to " +
            $"folder {mediaFile.FolderId}")
        {
            PerformedById = user.Id,
            Extended = mediaFile.Name,
        });

        logger.LogInformation("MediaFile {Name} ({Id}) restored by {Email}", mediaFile.Name, mediaFile.Id,
            user.Email);

        await database.SaveChangesAsync();

        return Ok();
    }

    [NonAction]
    private bool CanModify(MediaFile mediaFile, User user)
    {
        var groups = user.AccessCachedGroupsOrThrow();

        if (user.Id == mediaFile.UploadedById || user.Id == mediaFile.LastModifiedById)
        {
            return true;
        }

        if (groups.HasGroup(mediaFile.MetadataVisibility) && groups.HasGroup(mediaFile.ModifyAccess))
        {
            return true;
        }

        return false;
    }

    private class UploadVerifyToken
    {
        public long TargetItem { get; set; }
    }
}
