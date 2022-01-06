namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;

    public class CreateCrashReportData
    {
        [Required]
        [MaxLength(50)]
        public string ExitCode { get; set; }

        [Required]
        public long CrashTime { get; set; }

        [Required]
        public bool Public { get; set; }

        [Required]
        [MaxLength(100)]
        public string Platform { get; set; }

        public string LogFiles { get; set; }

        [StringLength(100, MinimumLength = 1)]
        public string Store { get; set; }

        [StringLength(100, MinimumLength = 1)]
        public string GameVersion { get; set; }

        [MaxLength(5000)]
        public string ExtraDescription { get; set; }

        [StringLength(255, MinimumLength = 1)]
        public string Email { get; set; }
    }
}
