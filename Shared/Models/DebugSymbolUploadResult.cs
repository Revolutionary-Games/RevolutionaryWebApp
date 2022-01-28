namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;

    public class DebugSymbolUploadResult
    {
        [Required]
        public string UploadUrl { get; set; } = string.Empty;

        [Required]
        [MaxLength(AppInfo.MaximumTokenLength)]
        public string VerifyToken { get; set; } = string.Empty;
    }
}
