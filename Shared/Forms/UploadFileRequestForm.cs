namespace RevolutionaryWebApp.Shared.Forms;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models.Enums;

public class UploadFileRequestForm
{
    [Required]
    [StringLength(120, MinimumLength = 3)]
    public string Name { get; set; } = string.Empty;

    public long? ParentFolder { get; set; }

    [Required]
    [Range(0, AppInfo.MaxGeneralFileStoreSize)]
    public long Size { get; set; }

    [Required]
    [MaxLength(150)]
    public string MimeType { get; set; } = string.Empty;

    [Required]
    public FileAccess ReadAccess { get; set; }

    [Required]
    public FileAccess WriteAccess { get; set; }
}
