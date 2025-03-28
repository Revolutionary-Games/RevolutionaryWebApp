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
///   Redirect for the main website to a different path
/// </summary>
public class PageRedirect : IUpdateNotifications
{
    public PageRedirect(string fromPath, string toUrl)
    {
        if (fromPath.StartsWith('/'))
            throw new ArgumentException("FromPath must not start with a slash", nameof(fromPath));

        if (fromPath.EndsWith('/'))
            throw new ArgumentException("FromPath must not end with a slash", nameof(fromPath));

        FromPath = fromPath;
        ToUrl = toUrl;
    }

    [Key]
    [AllowSortingBy]
    [MaxLength(256)]
    public string FromPath { get; set; }

    /// <summary>
    ///   Target URL, or if not starting with https, then this is a relative permalink redirect ("/ToUrl")
    /// </summary>
    [Required]
    [MaxLength(300)]
    [UpdateFromClientRequest]
    public string ToUrl { get; set; }

    [AllowSortingBy]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [AllowSortingBy]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string GetTarget(Uri? cdnBase)
    {
        if (ToUrl.StartsWith("https:"))
            return ToUrl;

        if (cdnBase == null)
        {
            if (!ToUrl.StartsWith("/"))
                ToUrl = "/" + ToUrl;

            return ToUrl;
        }

        if (!ToUrl.StartsWith("/"))
        {
            return new Uri(cdnBase, "/" + ToUrl).ToString();
        }

        return new Uri(cdnBase, ToUrl).ToString();
    }

    public PageRedirectDTO GetDTO()
    {
        return new PageRedirectDTO
        {
            FromPath = FromPath,
            ToUrl = ToUrl,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new PageRedirectListUpdated
            {
                Item = GetDTO(),
                Type = entityState.ToChangeType(),
            },
            NotificationGroups.PageRedirectListUpdated);
    }
}
