namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;

    public class LFSProjectInfo : ClientSideTimedModel
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Slug { get; set; } = string.Empty;

        public bool Public { get; set; }
        public long TotalObjectSize { get; set; }
    }
}
