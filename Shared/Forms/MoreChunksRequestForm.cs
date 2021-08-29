namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;

    public class MoreChunksRequestForm
    {
        [Required]
        [MaxLength(2000)]
        public string Token { get; set; }
    }
}
