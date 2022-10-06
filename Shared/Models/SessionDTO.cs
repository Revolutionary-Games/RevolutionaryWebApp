namespace ThriveDevCenter.Shared.Models;

using System.Net;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using SharedBase.Converters;

public class SessionDTO : ClientSideTimedModel
{
    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? LastUsedFrom { get; set; }

    public bool Current { get; set; }
}
