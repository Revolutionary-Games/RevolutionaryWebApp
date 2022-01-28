namespace ThriveDevCenter.Shared.Models
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    public class PatreonCredits
    {
        [Required]
        public List<string> VIPPatrons { get; set; } = new();

        [Required]
        public List<string> DevBuildPatrons { get; set; } = new();

        [Required]
        public List<string> SupporterPatrons { get; set; } = new();
    }
}
