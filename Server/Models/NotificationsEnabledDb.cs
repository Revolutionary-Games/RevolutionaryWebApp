namespace ThriveDevCenter.Server.Models
{
    using Microsoft.EntityFrameworkCore;
    using Services;

    public class NotificationsEnabledDb : ApplicationDbContext
    {
        public NotificationsEnabledDb(DbContextOptions<ApplicationDbContext> options,
            IModelUpdateNotificationSender notificationSender) : base(options)
        {
            AutoSendNotifications = notificationSender;
        }
    }
}
