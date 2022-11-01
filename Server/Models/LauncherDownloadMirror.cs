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
public class LauncherDownloadMirror : UpdateableModel, IUpdateNotifications
{
    public LauncherDownloadMirror(string internalName, Uri infoLink, string readableName)
    {
        InternalName = internalName;
        InfoLink = infoLink;
        ReadableName = readableName;
    }

    [AllowSortingBy]
    public string InternalName { get; }

    [AllowSortingBy]
    public Uri InfoLink { get; }

    public string ReadableName { get; }

    public Uri? BannerImageUrl { get; set; }

    public string? ExtraDescription { get; set; }

    public ICollection<LauncherVersionDownload> LauncherVersionDownloads { get; set; } =
        new HashSet<LauncherVersionDownload>();

    public ICollection<LauncherThriveVersionDownload> ThriveVersionDownloads { get; set; } =
        new HashSet<LauncherThriveVersionDownload>();

    public LauncherDownloadMirrorDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            InternalName = InternalName,
            InfoLink = InfoLink,
            ReadableName = ReadableName,
            BannerImageUrl = BannerImageUrl,
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
