namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class SentBulkEmailDTO : ClientSideModelWithCreationTime
{
    [Required]
    public string Title { get; set; } = string.Empty;

    public int Recipients { get; set; }
    public long? SentById { get; set; }
    public string? SystemSend { get; set; }
}
