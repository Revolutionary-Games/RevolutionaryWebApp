namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;

    public class UploadFileResponse
    {
        [Required]
        public string UploadURL { get; set; }

        [Required]
        public long TargetStorageItem { get; set; }

        [Required]
        public long TargetStorageItemVersion { get; set; }

        [Required]
        public string UploadVerifyToken { get; set; }
    }
}
