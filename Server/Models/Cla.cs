namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;
    using Utilities;

    [Index(nameof(Active))]
    public class Cla : BaseModel, IUpdateNotifications
    {
        [Required]
        public string RawMarkdown { get; set; }

        /// <summary>
        ///   The CLA that needs to be signed is active. Only one is able to be active at once
        /// </summary>
        [AllowSortingBy]
        public bool Active { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ClaSignature> Signatures { get; set; } = new HashSet<ClaSignature>();

        public CLAInfo GetInfo()
        {
            return new()
            {
                Id = Id,
                CreatedAt = CreatedAt,
                Active = Active,
            };
        }

        public CLADTO GetDTO()
        {
            return new()
            {
                Id = Id,
                CreatedAt = CreatedAt,
                Active = Active,
                RawMarkdown = RawMarkdown,
            };
        }

        public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
        {
            yield return new Tuple<SerializedNotification, string>(new CLAListUpdated()
            {
                Type = entityState.ToChangeType(),
                Item = GetInfo(),
            }, NotificationGroups.CLAListUpdated);

            yield return new Tuple<SerializedNotification, string>(
                new CLAUpdated() { Item = GetDTO() },
                NotificationGroups.CLAUpdatedPrefix + Id);
        }
    }
}
