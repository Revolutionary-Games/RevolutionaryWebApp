namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;

public class UploadFileResponse
{
    /// <summary>
    ///   The upload url to upload the file to. Set when Multipart is not set.
    /// </summary>
    public string? UploadURL { get; set; }

    [Required]
    public long TargetStorageItem { get; set; }

    [Required]
    public long TargetStorageItemVersion { get; set; }

    [Required]
    public string UploadVerifyToken { get; set; } = string.Empty;

    public MultipartFileUpload? Multipart { get; set; }
}
