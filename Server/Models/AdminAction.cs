namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Shared.Models;

    /// <summary>
    ///   A logged admin action
    /// </summary>
    [Index(nameof(PerformedById))]
    public class AdminAction : BaseModel
    {
        [Required]
        public string Message { get; set; } = string.Empty;

        [AllowSortingBy]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        ///   The user targeted in this action (maybe null). This is implicitly indexed
        /// </summary>
        [AllowSortingBy]
        public long? TargetUserId { get; set; }
        public User? TargetUser { get; set; }

        [AllowSortingBy]
        public long? PerformedById { get; set; }
        public User? PerformedBy { get; set; }

        public AdminActionDTO GetDTO()
        {
            return new()
            {
                Id = Id,
                Message = Message,
                CreatedAt = CreatedAt,
                TargetUserId = TargetUserId,
                PerformedById = PerformedById
            };
        }
    }
}
