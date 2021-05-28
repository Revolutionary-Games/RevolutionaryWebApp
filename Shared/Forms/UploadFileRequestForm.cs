namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;
    using Models;

    public class UploadFileRequestForm
    {
        [Required]
        [StringLength(120, MinimumLength = 3)]
        public string Name { get; set; }

        public long? ParentFolder { get; set; }

        [Required]
        [Range(0, AppInfo.MaxGeneralFileStoreSize)]
        public long Size { get; set; }

        [Required]
        public string MimeType { get; set; }

        [Required]
        public FileAccess ReadAccess { get; set; }

        [Required]
        public FileAccess WriteAccess { get; set; }
    }
}
