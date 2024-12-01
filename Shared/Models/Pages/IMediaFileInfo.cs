namespace RevolutionaryWebApp.Shared.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using Enums;

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

    public GroupType MetadataVisibility { get; set; }
    public GroupType ModifyAccess { get; set; }
    public long? UploadedById { get; set; }
    public bool Processed { get; set; }
    public bool Deleted { get; set; }
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
        MetadataVisibility = file.MetadataVisibility;
        ModifyAccess = file.ModifyAccess;
        UploadedById = file.UploadedById;
        Processed = file.Processed;
        Deleted = file.Deleted;
    }

    public MediaBrowserEntry(MediaFileDTO file)
    {
        Id = file.Id;
        CreatedAt = file.CreatedAt;
        UpdatedAt = file.UpdatedAt;
        Folder = false;
        Name = file.Name;
        GlobalId = file.GlobalId;
        MetadataVisibility = file.MetadataVisibility;
        ModifyAccess = file.ModifyAccess;
        UploadedById = file.UploadedById;
        Processed = file.Processed;
        Deleted = file.Deleted;
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

    public GroupType MetadataVisibility { get; set; }
    public GroupType ModifyAccess { get; set; }
    public long? UploadedById { get; set; }
    public bool Processed { get; set; }
    public bool Deleted { get; set; }

    public bool DeleteQueued { get; set; }

    [JsonIgnore]
    public string IdWithFolder => Id + "-" + Folder;
}
