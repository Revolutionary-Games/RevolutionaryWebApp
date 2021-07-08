namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;

    public class CLADTO : ClientSideModel
    {
        public DateTime CreatedAt { get; set; }
        public bool Active { get; set; }

        [MaxLength(AppInfo.MEBIBYTE * 2)]
        [Required]
        public string RawMarkdown { get; set; }
    }
}
