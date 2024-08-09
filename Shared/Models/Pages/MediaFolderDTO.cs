namespace RevolutionaryWebApp.Shared.Models.Pages;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using Enums;
using SharedBase.ModelVerifiers;

public class MediaFolderDTO : ClientSideTimedModel
{
    [Required]
    [StringLength(80, MinimumLength = 3)]
    [MayNotContain("/")]
    public string Name { get; set; } = string.Empty;

    public long? ParentFolderId { get; set; }
    public GroupType ContentWriteAccess { get; set; }
    public GroupType ContentReadAccess { get; set; }
    public GroupType SubFolderModifyAccess { get; set; }
    public GroupType FolderModifyAccess { get; set; }
    public long? OwnedById { get; set; }
    public long? LastModifiedById { get; set; }

    public bool DeleteIfEmpty { get; set; }
}
