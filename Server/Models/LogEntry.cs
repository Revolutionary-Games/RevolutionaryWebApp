namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Shared;

    /// <summary>
    ///   Important automated (non-messed) with log messages
    /// </summary>
    public class LogEntry : BaseModel
    {
        [Required]
        public string Message { get; set; }

        [AllowSortingBy]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        ///   The user targeted in this entry (maybe null). This is implicitly indexed
        /// </summary>
        public long? TargetUserId { get; set; }
        public User TargetUser { get; set; }
    }
}
