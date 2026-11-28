namespace RevolutionaryWebApp.Shared.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using Enums;
using Utilities;

public interface IMediaFileInfo
{
    public string Name { get; }
    public Guid GlobalId { get; }
}

public class MediaFileInfo : ClientSideTimedModel, IMediaFileInfo
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public Guid GlobalId { get; set; }

    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }

    public GroupType MetadataVisibility { get; set; }
    public GroupType ModifyAccess { get; set; }
    public long? UploadedById { get; set; }
    public long? LastModifiedById { get; set; }
    public bool Processed { get; set; }
    public bool AllowLossyWebpPreviews { get; set; }
    public bool Deleted { get; set; }
    public bool PurgeStarted { get; set; }
}

/// <summary>
///   Unified model for both folders and media files
/// </summary>
public class MediaBrowserEntry : ClientSideTimedModel
{
    public MediaBrowserEntry(MediaFileInfo file)
    {
        Id = file.Id;
        CreatedAt = file.CreatedAt;
        UpdatedAt = file.UpdatedAt;
        Folder = false;
        Name = file.Name;
        GlobalId = file.GlobalId;
        OriginalWidth = file.OriginalWidth;
        OriginalHeight = file.OriginalHeight;
        MetadataVisibility = file.MetadataVisibility;
        ModifyAccess = file.ModifyAccess;
        UploadedById = file.UploadedById;
        LastModifiedById = file.LastModifiedById;
        Processed = file.Processed;
        AllowLossyWebpPreviews = file.AllowLossyWebpPreviews;
        Deleted = file.Deleted;
        PurgeStarted = file.PurgeStarted;
    }

    public MediaBrowserEntry(MediaFileDTO file)
    {
        Id = file.Id;
        CreatedAt = file.CreatedAt;
        UpdatedAt = file.UpdatedAt;
        Folder = false;
        Name = file.Name;
        GlobalId = file.GlobalId;
        OriginalWidth = file.OriginalWidth;
        OriginalHeight = file.OriginalHeight;
        MetadataVisibility = file.MetadataVisibility;
        ModifyAccess = file.ModifyAccess;
        UploadedById = file.UploadedById;
        LastModifiedById = file.LastModifiedById;
        Processed = file.Processed;
        AllowLossyWebpPreviews = file.AllowLossyWebpPreviews;
        Deleted = file.Deleted;
        PurgeStarted = file.PurgeStarted;
    }

    public MediaBrowserEntry(MediaFolderInfo folder)
    {
        Id = folder.Id;
        CreatedAt = folder.CreatedAt;
        UpdatedAt = folder.UpdatedAt;
        Folder = true;
        Name = folder.Name;
        MetadataVisibility = folder.ContentReadAccess;
        ModifyAccess = folder.ContentWriteAccess;
        UploadedById = folder.OwnedById;
        Processed = true;
        Deleted = false;
        DeleteQueued = folder.DeleteIfEmpty;
    }

    [JsonConstructor]
    public MediaBrowserEntry()
    {
    }

    public bool Folder { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public Guid GlobalId { get; set; }

    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }

    public GroupType MetadataVisibility { get; set; }
    public GroupType ModifyAccess { get; set; }
    public long? UploadedById { get; set; }
    public long? LastModifiedById { get; set; }
    public bool Processed { get; set; }
    public bool AllowLossyWebpPreviews { get; set; }
    public bool Deleted { get; set; }
    public bool PurgeStarted { get; set; }

    public bool DeleteQueued { get; set; }

    [JsonIgnore]
    public string IdWithFolder => Id + "-" + Folder;

    public string GetMarkdown(bool allowFullSize = true)
    {
        // For now only implemented for image-type entries
        var sanitizedName = Name.Replace(']', '_').Replace('[', '_');

        var extension = Path.GetExtension(Name);

        // PNGs always use webp previews for more efficient bandwidth usage
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            extension = ".webp";

        var (width, height) = GetPageFitDimensions();
        var destination = MediaLinkParser.FormatDestination(extension, GlobalId, allowFullSize, width, height);
        return $"![{sanitizedName}]({destination})";
    }

    private (int Width, int Height) GetPageFitDimensions()
    {
        if (OriginalWidth <= 0 || OriginalHeight <= 0)
            return (0, 0);

        // Match the preview generator, which constrains width first and otherwise constrains height.
        const int limit = AppInfo.MediaResolutionPage;
        if (OriginalWidth > limit)
            return (limit, Math.Max(1, (int)Math.Round(OriginalHeight * (double)limit / OriginalWidth)));

        if (OriginalHeight > limit)
            return (Math.Max(1, (int)Math.Round(OriginalWidth * (double)limit / OriginalHeight)), limit);

        return (OriginalWidth, OriginalHeight);
    }
}
