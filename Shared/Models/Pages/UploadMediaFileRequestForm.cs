namespace RevolutionaryWebApp.Shared.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using Enums;
using SharedBase.ModelVerifiers;

public class UploadMediaFileRequestForm
{
    [Required]
    public Guid MediaFileId { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(100, MinimumLength = 3)]
    [MustContain(".")]
    public string Name { get; set; } = string.Empty;

    public long Folder { get; set; }

    [Required]
    [Range(80, AppInfo.MaxMediaFileSize)]
    public long Size { get; set; }

    [Required]
    public GroupType MetadataVisibility { get; set; }

    [Required]
    public GroupType ModifyAccess { get; set; }
}
