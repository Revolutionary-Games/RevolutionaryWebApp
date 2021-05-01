namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;

    public class TokenForm
    {
        [Required]
        public string Token { get; set; }
    }
}
