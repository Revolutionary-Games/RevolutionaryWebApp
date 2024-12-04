namespace RevolutionaryWebApp.Server.Models.Pages;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models.Pages;
using Shared.Notifications;
using Utilities;

/// <summary>
///   Part of the site layout like sidebar and top links (these are specified in the DB to allow easy modification of
///   these)
/// </summary>
public class SiteLayoutPart : UpdateableModel, IUpdateNotifications
{
    public SiteLayoutPart(string linkTarget, string altText, SiteLayoutPartType partType)
    {
        if (string.IsNullOrWhiteSpace(linkTarget))
            throw new ArgumentException("Link target must be provided", nameof(linkTarget));

        LinkTarget = linkTarget;
        AltText = altText;
        PartType = partType;
    }

    [MaxLength(255)]
    [AllowSortingBy]
    [UpdateFromClientRequest]
    public string LinkTarget { get; set; }

    /// <summary>
    ///   Alt text or the text that represents this link if there's no <see cref="ImageId"/>
    /// </summary>
    [MaxLength(100)]
    [AllowSortingBy]
    [UpdateFromClientRequest]
    public string AltText { get; set; }

    [AllowSortingBy]
    [UpdateFromClientRequest]
    public SiteLayoutPartType PartType { get; set; }

    public Guid? ImageId { get; set; }

    public MediaFile? Image { get; set; }

    /// <summary>
    ///   When set to false doesn't get rendered on pages
    /// </summary>
    [AllowSortingBy]
    [UpdateFromClientRequest]
    public bool Enabled { get; set; } = true;

    public SiteLayoutPartDTO GetDTO()
    {
        return new SiteLayoutPartDTO
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            LinkTarget = LinkTarget,
            AltText = AltText,
            PartType = PartType,
            ImageId = ImageId?.ToString(),
            Enabled = Enabled,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new SiteLayoutPartUpdated
            {
                Item = GetDTO(),
            },
            NotificationGroups.LayoutPartUpdated);
    }
}
