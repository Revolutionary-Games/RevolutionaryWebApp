namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;

    public class EmailVerificationFinishForm
    {
        [Required]
        public string Token { get; set; }
    }
}
