namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;

    public class JSONWrappedRedirect
    {
        [Required]
        public string RedirectTo { get; set; } = string.Empty;
    }
}
