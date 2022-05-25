namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;
    using ModelVerifiers;

    public class EmailTestRequestForm
    {
        [Required]
        [Email]
        public string Recipient { get; set; } = string.Empty;
    }
}
