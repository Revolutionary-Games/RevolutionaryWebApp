namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;

    public class RegistrationFormData
    {
        [Required]
        [StringLength(AppInfo.MaxEmailLength, MinimumLength = 3)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(AppInfo.MaxUsernameLength, MinimumLength = AppInfo.MinUsernameLength)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(AppInfo.MaxPasswordLength, MinimumLength = AppInfo.MinPasswordLength)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [MaxLength(AppInfo.MaximumTokenLength)]
        public string CSRF { get; set; } = string.Empty;

        [MaxLength(300)]
        public string? RegistrationCode { get; set; }
    }
}
