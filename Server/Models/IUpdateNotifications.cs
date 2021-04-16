namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.Collections.Generic;
    using Microsoft.EntityFrameworkCore;
    using Shared.Notifications;

    public interface IUpdateNotifications
    {
        /// <summary>
        ///   Some model types can be marked deleted before they are actually removed from the database for safety
        ///   reasons as it is then easy to notice that something has been deleted and restore it before it is hard
        ///   deleted from the database
        /// </summary>
        public virtual bool UsesSoftDelete
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        ///   Returns the soft delete status if UsesSoftDelete is true
        /// </summary>
        public virtual bool IsSoftDeleted
        {
            get
            {
                return false;
            }
        }

        public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState);
    }
}
