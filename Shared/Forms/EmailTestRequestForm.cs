namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;

    public class EmailTestRequestForm
    {
        [Required]
        [StringLength(AppInfo.MaxEmailLength, MinimumLength = 3)]
        public string Recipient { get; set; }
    }
}
