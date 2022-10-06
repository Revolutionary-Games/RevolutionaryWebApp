namespace ThriveDevCenter.Shared.Models;

using System;
using DevCenterCommunication.Models;

public class CLAInfo : ClientSideModel
{
    public DateTime CreatedAt { get; set; }
    public bool Active { get; set; }
}
