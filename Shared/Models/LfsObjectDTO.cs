namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class LfsObjectDTO : ClientSideTimedModel
{
    [Required]
    public string LfsOid { get; set; } = string.Empty;

    public long Size { get; set; }
}
