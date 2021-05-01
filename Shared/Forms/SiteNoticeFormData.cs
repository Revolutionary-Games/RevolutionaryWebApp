namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;

    public class SiteNoticeFormData
    {
        [Required]
        [MaxLength(140)]
        public string Message { get; set; }

        [Required]
        public SiteNoticeType Type { get; set; }
    }
}
