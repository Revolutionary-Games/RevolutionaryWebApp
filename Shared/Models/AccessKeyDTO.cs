namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using SharedBase.Converters;

public class AccessKeyDTO : ClientSideTimedModel
{
    [Required]
    public string Description { get; set; } = string.Empty;
    public DateTime? LastUsed { get; set; }
    public AccessKeyType KeyType { get; set; }

    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? LastUsedFrom { get; set; }
}
