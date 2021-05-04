namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.Net;
    using Shared.Models;

    public class ControlledServer : UpdateableModel
    {
        public ServerStatus Status { get; set; } = ServerStatus.Provisioning;
        public DateTime StatusLastChecked { get; set; }= DateTime.UtcNow;

        public ServerReservationType ReservationType { get; set; } = ServerReservationType.None;
        public long? ReservedFor { get; set; }

        /// <summary>
        ///   When running has the address to connect to the server
        /// </summary>
        public IPAddress PublicAddress { get; set; }

        public DateTime? RunningSince { get; set; }

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
        public bool WantsMaintenance { get; set; }

        public DateTime LastMaintenance { get; set; } = DateTime.UtcNow;
    }
}
