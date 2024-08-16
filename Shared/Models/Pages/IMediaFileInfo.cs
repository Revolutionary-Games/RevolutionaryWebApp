namespace RevolutionaryWebApp.Shared.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
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
