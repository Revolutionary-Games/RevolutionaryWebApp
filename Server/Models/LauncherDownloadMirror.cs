namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using DevCenterCommunication.Models;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

/// <summary>
///   Serverside data for <see cref="DownloadMirrorInfo"/>
/// </summary>
/// <remarks>
///   <para>
///     Note that the Ids should be less than 56000 due to the way individual downloads generate IDs on the fly.
///   </para>
/// </remarks>
[Index(nameof(InternalName), IsUnique = true)]
public class LauncherDownloadMirror : UpdateableModel, IUpdateNotifications
{
    public LauncherDownloadMirror(string internalName, Uri infoLink, string readableName)
    {
        InternalName = internalName;
        InfoLink = infoLink;
        ReadableName = readableName;
    }

    [AllowSortingBy]
    public string InternalName { get; private set; }

    [AllowSortingBy]
    [UpdateFromClientRequest]
    [ConvertWithWhenUpdatingFromClient(nameof(StringToUri))]
    public Uri InfoLink { get; set; }

    [AllowSortingBy]
    [UpdateFromClientRequest]
    public string ReadableName { get; set; }

    [UpdateFromClientRequest]
    [ConvertWithWhenUpdatingFromClient(nameof(StringToUri))]
    public Uri? BannerImageUrl { get; set; }

    [UpdateFromClientRequest]
    public string? ExtraDescription { get; set; }

    public ICollection<LauncherVersionDownload> LauncherVersionDownloads { get; set; } =
        new HashSet<LauncherVersionDownload>();

    public ICollection<LauncherThriveVersionDownload> ThriveVersionDownloads { get; set; } =
        new HashSet<LauncherThriveVersionDownload>();

    public static Uri StringToUri(string value)
    {
        return new Uri(value);
    }

    public LauncherDownloadMirrorDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            InternalName = InternalName,
            InfoLink = InfoLink.ToString(),
            ReadableName = ReadableName,
            BannerImageUrl = BannerImageUrl?.ToString(),
            ExtraDescription = ExtraDescription,
        };
    }

    public DownloadMirrorInfo GetLauncherInfo()
    {
        return new(InfoLink, ReadableName)
        {
            BannerImage = BannerImageUrl,
            ExtraDescription = ExtraDescription,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new LauncherDownloadMirrorListUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(),
        }, NotificationGroups.LauncherDownloadMirrorListUpdated);

        yield return new Tuple<SerializedNotification, string>(new LauncherDownloadMirrorUpdated
        {
            Item = GetDTO(),
        }, NotificationGroups.LauncherDownloadMirrorUpdatedPrefix + Id);
    }
}
