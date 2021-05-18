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
    public class ActionLogEntry : BaseModel
    {
        [Required]
        public string Message { get; set; }

        [AllowSortingBy]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [AllowSortingBy]
        public long? PerformedById { get; set; }

        public User PerformedBy { get; set; }

        public ActionLogEntryDTO GetDTO()
        {
            return new()
            {
                Id = Id,
                Message = Message,
                CreatedAt = CreatedAt,
                PerformedById = PerformedById
            };
        }
    }
}
