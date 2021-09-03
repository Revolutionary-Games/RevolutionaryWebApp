namespace ThriveDevCenter.Shared.Models
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    public class PatreonCredits
    {
        [Required]
        public List<string> VIPPatrons { get; set; }

        [Required]
        public List<string> DevBuildPatrons { get; set; }

        [Required]
        public List<string> SupporterPatrons { get; set; }
    }
}
