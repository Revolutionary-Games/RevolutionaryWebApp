namespace RevolutionaryWebApp.Shared.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class PageVersionInfo : IIdentifiable
{
    public long PageId { get; set; }

    public int Version { get; set; }

    [MaxLength(AppInfo.MaxPageEditCommentLength)]
    public string? EditComment { get; set; }

    public bool Deleted { get; set; }

    public long? EditedById { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    ///   This fake ID generation assumes that there aren't that many different pages and not all bits of the version
    ///   are used.
    /// </summary>
    public long Id => PageId | ((long)Version << 38);
}
