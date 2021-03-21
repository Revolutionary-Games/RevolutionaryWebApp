namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;
    using Shared;

    /// <summary>
    ///   A logged admin action
    /// </summary>
    [Index(nameof(PerformedById))]
    public class AdminAction : BaseModel
    {
        [Required]
        public string Message { get; set; }

        [AllowSortingBy]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        ///   The user targeted in this action (maybe null). This is implicitly indexed
        /// </summary>
        public long? TargetUserId { get; set; }
        public User TargetUser { get; set; }

        public long? PerformedById { get; set; }
        public User PerformedBy { get; set; }
    }
}
