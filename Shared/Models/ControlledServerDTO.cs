namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.Net;

    public class ControlledServerDTO : ClientSideTimedModel
    {
        public ServerStatus Status { get; set; }
        public DateTime StatusLastChecked { get; set; }
        public ServerReservationType ReservationType { get; set; }
        public IPAddress PublicAddress { get; set; }
        public DateTime? RunningSince { get; set; }
        public double TotalRuntime { get; set; }
        public bool ProvisionedFully { get; set; }
        public string InstanceId { get; set; }
        public bool WantsMaintenance { get; set; }
        public DateTime LastMaintenance { get; set; }
    }
}
