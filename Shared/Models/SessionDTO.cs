namespace ThriveDevCenter.Shared.Models;

using System.Net;
using System.Text.Json.Serialization;
using SharedBase.Converters;

public class SessionDTO : ClientSideTimedModel
{
    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? LastUsedFrom { get; set; }

    public bool Current { get; set; }
}
