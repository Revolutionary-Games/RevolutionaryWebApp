namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Net;
    using System.Text.Json.Serialization;
    using Shared;
    using Shared.Converters;
    using Shared.Models;

    /// <summary>
    ///   Common data for controlled and external servers
    /// </summary>
    public abstract class BaseServer : UpdateableModel
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

        public bool ProvisionedFully { get; set; }

        /// <summary>
        ///   This is percentage of the used disk space
        /// </summary>
        [AllowSortingBy]
        public int UsedDiskSpace { get; set; } = -1;

        public bool CleanUpQueued { get; set; }

        /// <summary>
        ///   If true no new jobs are allowed to start
        /// </summary>
        [AllowSortingBy]
        public bool WantsMaintenance { get; set; }

        [AllowSortingBy]
        public DateTime LastMaintenance { get; set; } = DateTime.UtcNow;

        [NotMapped]
        public abstract bool IsExternal { get; }

        public void MarkAsProvisioningStarted()
        {
            var now = DateTime.UtcNow;

            ProvisionedFully = false;
            Status = ServerStatus.Provisioning;
            LastMaintenance = now;
            StatusLastChecked = now;
            this.BumpUpdatedAt();
        }
    }
}
