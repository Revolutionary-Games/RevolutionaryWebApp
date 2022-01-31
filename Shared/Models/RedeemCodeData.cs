namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;

    public class RedeemCodeData
    {
        [Required]
        public string Code { get; set; } = string.Empty;
    }
}
