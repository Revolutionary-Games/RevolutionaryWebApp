namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Shared.Models.Enums;

    public class PullRequestAutoComment : UpdateableModel
    {
        [Required]
        public string Comment { get; set; }

        public WhenToAutoComment When { get; set; }

        public bool Active { get; set; } = true;
    }
}
