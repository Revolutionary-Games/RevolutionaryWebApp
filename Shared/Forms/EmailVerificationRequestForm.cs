namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;

    public class EmailVerificationRequestForm
    {
        [Required]
        [StringLength(AppInfo.MaxEmailLength, MinimumLength = 3)]
        public string Email { get; set; }
    }
}
