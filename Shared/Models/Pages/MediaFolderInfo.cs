namespace RevolutionaryWebApp.Shared.Models.Pages;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using Enums;

public class MediaFolderInfo : ClientSideTimedModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public long? ParentFolderId { get; set; }
    public GroupType ContentWriteAccess { get; set; }
    public GroupType ContentReadAccess { get; set; }
    public long? OwnedById { get; set; }
    public long? LastModifiedById { get; set; }

    public bool DeleteIfEmpty { get; set; }
}
