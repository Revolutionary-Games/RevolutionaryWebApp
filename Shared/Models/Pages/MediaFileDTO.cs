namespace RevolutionaryWebApp.Shared.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using DevCenterCommunication.Models;
using Enums;

public class MediaFileDTO : ClientSideTimedModel, IMediaFileInfo
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    public Guid GlobalId { get; set; }
    public GroupType MetadataVisibility { get; set; }
    public GroupType ModifyAccess { get; set; }
    public long? UploadedById { get; set; }
    public long? LastModifiedById { get; set; }
    public bool Processed { get; set; }
    public bool Deleted { get; set; }
}
