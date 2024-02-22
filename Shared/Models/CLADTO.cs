namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using SharedBase.Utilities;

public class CLADTO : ClientSideModel
{
    public DateTime CreatedAt { get; set; }
    public bool Active { get; set; }

    [MaxLength(GlobalConstants.MEBIBYTE * 2)]
    [Required]
    public string RawMarkdown { get; set; } = string.Empty;
}
