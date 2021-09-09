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

    [Index(nameof(CreatedAt))]
    public class SentBulkEmail : ModelWithCreationTime, IUpdateNotifications
    {
        [Required]
        public string Title { get; set; }

        public int Recipients { get; set; }

        [AllowSortingBy]
        public long? SentById { get; set; }

        /// <summary>
        ///   Set if the system automatically sent this bulk email
        /// </summary>
        [AllowSortingBy]
        public string SystemSend { get; set; }

        public User SentBy { get; set; }

        public SentBulkEmailDTO GetDTO()
        {
            return new()
            {
                Id = Id,
                CreatedAt = CreatedAt,
                Title = Title,
                Recipients = Recipients,
                SentById = SentById,
                SystemSend = SystemSend,
            };
        }

        public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
        {
            yield return new Tuple<SerializedNotification, string>(new SentBulkEmailListUpdated()
            {
                Type = entityState.ToChangeType(),
                Item = GetDTO(),
            }, NotificationGroups.SentBulkEmailListUpdated);
        }
    }
}
