namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Text.Json.Serialization;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Shared.Converters;
    using Shared.Models;
    using Shared.Notifications;
    using Utilities;

    public class ControlledServer : UpdateableModel, IUpdateNotifications
    {
        [AllowSortingBy]
        public ServerStatus Status { get; set; } = ServerStatus.Provisioning;

        [AllowSortingBy]
        public DateTime StatusLastChecked { get; set; } = DateTime.UtcNow;

        [AllowSortingBy]
        public ServerReservationType ReservationType { get; set; } = ServerReservationType.None;

        public long? ReservedFor { get; set; }

        /// <summary>
        ///   When running has the address to connect to the server
        /// </summary>
        [JsonConverter(typeof(IPAddressConverter))]
        public IPAddress PublicAddress { get; set; }

        [AllowSortingBy]
        public DateTime? RunningSince { get; set; }

        [AllowSortingBy]
        public double TotalRuntime { get; set; } = 0.0;

        // TODO: hook these two up so that a maintenance job can recreate outdated servers
        public string CreatedWithImage { get; set; }
        public string AWSInstanceType { get; set; }
        public long CreatedVolumeSize { get; set; }

        public bool ProvisionedFully { get; set; }

        public string InstanceId { get; set; }

        // TODO: implement detecting this and adjusting volume size if not enough
        public long AvailableDiskSpace { get; set; }

        /// <summary>
        ///   If true no new jobs are allowed to start
        /// </summary>
        [AllowSortingBy]
        public bool WantsMaintenance { get; set; }

        [AllowSortingBy]
        public DateTime LastMaintenance { get; set; } = DateTime.UtcNow;

        public void SetProvisioningStatus(string instanceId)
        {
            var now = DateTime.UtcNow;

            InstanceId = instanceId;
            ProvisionedFully = false;
            Status = ServerStatus.Provisioning;
            LastMaintenance = now;
            StatusLastChecked = now;
            this.BumpUpdatedAt();
        }

        public ControlledServerDTO GetDTO()
        {
            return new()
            {
                Id = Id,
                Status = Status,
                StatusLastChecked = StatusLastChecked,
                ReservationType = ReservationType,
                PublicAddress = PublicAddress,
                RunningSince = RunningSince,
                TotalRuntime = TotalRuntime,
                ProvisionedFully = ProvisionedFully,
                InstanceId = InstanceId,
                WantsMaintenance = WantsMaintenance,
                LastMaintenance = LastMaintenance,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt
            };
        }

        public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
        {
            yield return new Tuple<SerializedNotification, string>(new ControlledServersUpdated()
            {
                Type = entityState.ToChangeType(),
                Item = GetDTO()
            }, NotificationGroups.ControlledServerListUpdated);
        }
    }
}
