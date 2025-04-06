namespace RevolutionaryWebApp.Server.Models.Pages;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models.Pages;
using Shared.Notifications;
using Shared.Services;
using Utilities;

/// <summary>
///   Part of the site layout like sidebar and top links (these are specified in the DB to allow easy modification of
///   these)
/// </summary>
[Index(nameof(PartType), nameof(Order), IsUnique = true)]
public class SiteLayoutPart : UpdateableModel, IUpdateNotifications
{
    public SiteLayoutPart(string? linkTarget, string altText, SiteLayoutPartType partType)
    {
        if (string.IsNullOrWhiteSpace(linkTarget) && string.IsNullOrWhiteSpace(altText))
            throw new ArgumentException("Link target or text must be provided", nameof(linkTarget));

        LinkTarget = linkTarget;
        AltText = altText;
        PartType = partType;
    }

    [MaxLength(255)]
    [AllowSortingBy]
    [UpdateFromClientRequest]
    public string? LinkTarget { get; set; }

    /// <summary>
    ///   Alt text or the text that represents this link if there's no <see cref="ImageId"/>
    /// </summary>
    [MaxLength(128)]
    [AllowSortingBy]
    [UpdateFromClientRequest]
    public string AltText { get; set; }

    [AllowSortingBy]
    [UpdateFromClientRequest]
    public SiteLayoutPartType PartType { get; set; }

    [UpdateFromClientRequest]
    public LayoutPartDisplayMode DisplayMode { get; set; }

    public Guid? ImageId { get; set; }

    public MediaFile? Image { get; set; }

    /// <summary>
    ///   Extension of the image file. Defaults to ".png" if not defined
    /// </summary>
    [MaxLength(64)]
    public string? ImageType { get; set; }

    /// <summary>
    ///   Lower order items are shown first within their respective <see cref="PartType"/> (required to be unique)
    /// </summary>
    [UpdateFromClientRequest]
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
            DisplayMode = DisplayMode,
            PartType = PartType,
            ImageId = ImageId?.ToString(),
            Order = Order,
            Enabled = Enabled,
        };
    }

    public RenderingLayoutPart GetRenderingData(string activeLink, IMediaLinkConverter linkConverter)
    {
        bool isPageLink = false;
        string? linkTarget = null;
        bool active = false;

        if (LinkTarget != null)
        {
            isPageLink = LinkTarget.StartsWith("page:");

            if (isPageLink)
            {
                var permalink = LinkTarget.Substring(5);

                if (permalink == AppInfo.IndexPermalinkName)
                    permalink = string.Empty;

                linkTarget = $"{linkConverter.GetInternalPageLinkPrefix()}/{permalink}";

                // Detecting the active link is pretty complex as the special index value needs handling
                active = LinkTarget.EndsWith(activeLink) ||
                    (permalink == AppInfo.IndexPermalinkName && string.IsNullOrEmpty(activeLink)) ||
                    ((permalink == "/" || string.IsNullOrEmpty(permalink)) &&
                        (activeLink == AppInfo.IndexPermalinkName || string.IsNullOrEmpty(activeLink)));
            }
            else
            {
                linkTarget = LinkTarget;
            }
        }

        return new RenderingLayoutPart(linkTarget, AltText)
        {
            Active = active,

            Image = ImageId != null ?
                linkConverter.TranslateImageLink(ImageType ?? ".png", ImageId.Value.ToString(), MediaFileSize.FitPage) :
                null,

            DisplayMode = DisplayMode,

            // Order should be followed, but just in case this data is ever needed, it is copied
            Order = Order,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        // The list is always fully viewed, so this is not useful and not sent as a result
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

/// <summary>
///   Info from a <see cref="SiteLayoutPart"/> that is necessary for rendering on a page
/// </summary>
public class RenderingLayoutPart
{
    public bool Active;
    public string? LinkTarget;
    public string AltText;
    public LayoutPartDisplayMode DisplayMode;
    public string? Image;
    public int Order;

    public RenderingLayoutPart(string? linkTarget, string altText)
    {
        LinkTarget = linkTarget;
        AltText = altText;
    }
}
