namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;
    using ModelVerifiers;

    public class RegistrationFormData
    {
        [Required]
        [Email]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(AppInfo.MaxUsernameLength, MinimumLength = AppInfo.MinUsernameLength)]
        [NoTrailingOrPrecedingSpace]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(AppInfo.MaxPasswordLength, MinimumLength = AppInfo.MinPasswordLength)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [MaxLength(AppInfo.MaximumTokenLength)]
        public string CSRF { get; set; } = string.Empty;

        [MaxLength(300)]
        [NoTrailingOrPrecedingSpace]
        public string? RegistrationCode { get; set; }
    }
}
