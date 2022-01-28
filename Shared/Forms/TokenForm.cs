namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;

    public class TokenForm
    {
        [Required]
        [MaxLength(AppInfo.MaximumTokenLength)]
        public string Token { get; set; } = string.Empty;
    }
}
