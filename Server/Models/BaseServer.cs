namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Net;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using Shared;
using Shared.Models;
using SharedBase.Converters;

/// <summary>
///   Common data for controlled servers (external servers is a removed feature, replaced with runners)
/// </summary>
public abstract class BaseServer : UpdateableModel
{
    [AllowSortingBy]
    public ServerStatus Status { get; set; } = ServerStatus.Provisioning;

    [AllowSortingBy]
    public DateTime StatusLastChecked { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///   When running has the address to connect to the server
    /// </summary>
    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? PublicAddress { get; set; }

    [AllowSortingBy]
    public DateTime? RunningSince { get; set; }

    public bool ProvisionedFully { get; set; }

    /// <summary>
    ///   This is the percentage of the used disk space
    /// </summary>
    [AllowSortingBy]
    public int UsedDiskSpace { get; set; } = -1;

    public bool CleanUpQueued { get; set; }

    /// <summary>
    ///   If true, no new jobs are allowed to start
    /// </summary>
    [AllowSortingBy]
    public bool WantsMaintenance { get; set; }

    [AllowSortingBy]
    public DateTime LastMaintenance { get; set; } = DateTime.UtcNow;

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
