namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

public class CiProject : UpdateableModel, IUpdateNotifications, ISoftDeletable, IInfoCreator<CIProjectInfo>,
    IDTOCreator<CIProjectDTO>
{
    [Required]
    [AllowSortingBy]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string RepositoryFullName { get; set; } = string.Empty;

    [Required]
    public string RepositoryCloneUrl { get; set; } = string.Empty;

    [Required]
    public CIProjectType ProjectType { get; set; }

    [Required]
    public string DefaultBranch { get; set; } = "master";

    public bool Public { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public bool Deleted { get; set; } = false;

    public ICollection<CiBuild> CiBuilds { get; set; } = new HashSet<CiBuild>();

    public ICollection<CiSecret> CiSecrets { get; set; } = new HashSet<CiSecret>();

    [NotMapped]
    public bool UsesSoftDelete => true;

    [NotMapped]
    public bool IsSoftDeleted => Deleted;

    public CIProjectInfo GetInfo()
    {
        return new()
        {
            Id = Id,
            Name = Name,
            Public = Public,
        };
    }

    public CIProjectDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            Name = Name,
            RepositoryFullName = RepositoryFullName,
            ProjectType = ProjectType,
            Public = Public,
            Deleted = Deleted,
            Enabled = Enabled,
            RepositoryCloneUrl = RepositoryCloneUrl,
            DefaultBranch = DefaultBranch,
            UpdatedAt = UpdatedAt,
            CreatedAt = CreatedAt,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        // Skip sending normal updates if this is in deleted state (and didn't currently become undeleted
        // or deleted)
        if (entityState != EntityState.Modified || !Deleted)
        {
            var listGroup = Public ?
                NotificationGroups.CIProjectListUpdated :
                NotificationGroups.PrivateCIProjectUpdated;
            yield return new Tuple<SerializedNotification, string>(new CIProjectListUpdated
                    { Type = entityState.ToChangeType(), Item = GetInfo() },
                listGroup);
        }

        // TODO: should there be a separate groups for private and deleted items as if someone joins the
        // notification group before this goes into a state where they couldn't join anymore, they still receive
        // notifications and that leaks some information
        yield return new Tuple<SerializedNotification, string>(
            new CIProjectUpdated { Item = GetDTO() },
            NotificationGroups.CIProjectUpdatedPrefix + Id);
    }
}