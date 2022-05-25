namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;
    using ModelVerifiers;

    public class EmailVerificationRequestForm
    {
        [Required]
        [Email]
        public string Email { get; set; } = string.Empty;
    }
}
