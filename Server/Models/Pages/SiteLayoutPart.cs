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
[Index(nameof(PartType), nameof(Order), IsUnique = true)]
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
    ///   Lower order items are shown first within their respective <see cref="PartType"/> (required to be unique)
    /// </summary>
    public int Order { get; set; }

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
            Order = Order,
            Enabled = Enabled,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        // The list is always fully viewed so this is not useful and not sent as a result
        // yield return new Tuple<SerializedNotification, string>(new SiteLayoutPartUpdated
        //     {
        //         Item = GetDTO(),
        //     },
        //     NotificationGroups.LayoutPartUpdated);

        yield return new Tuple<SerializedNotification, string>(new SiteLayoutListUpdated
            {
                Item = GetDTO(),
                Type = entityState.ToChangeType(),
            },
            NotificationGroups.LayoutPartUpdated);
    }
}
