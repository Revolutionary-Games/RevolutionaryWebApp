namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;

    public class DebugSymbolUploadResult
    {
        [Required]
        public string UploadUrl { get; set; }

        [Required]
        public string VerifyToken { get; set; }
    }
}
