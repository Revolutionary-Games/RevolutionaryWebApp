namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using SharedBase.Converters;

public class ControlledServerDTO : ClientSideTimedModel
{
    public ServerStatus Status { get; set; }
    public DateTime StatusLastChecked { get; set; }
    public ServerReservationType ReservationType { get; set; }

    [Required]
    public string ReservedFor { get; set; } = string.Empty;

    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? PublicAddress { get; set; }

    public DateTime? RunningSince { get; set; }
    public double TotalRuntime { get; set; }
    public bool ProvisionedFully { get; set; }
    public string? InstanceId { get; set; }
    public bool WantsMaintenance { get; set; }
    public DateTime LastMaintenance { get; set; }
    public int UsedDiskSpace { get; set; }
    public bool CleanUpQueued { get; set; }
}
