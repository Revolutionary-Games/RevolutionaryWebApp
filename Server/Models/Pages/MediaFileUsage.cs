namespace RevolutionaryWebApp.Server.Models.Pages;

using System;

/// <summary>
///   Marks a media file as used to prevent deleting it accidentally. Note that not all usages go through this extra
///   table. Some usages are many-to-many, so they use this helper table.
/// </summary>
public class MediaFileUsage
{
    public enum UsageType
    {
        Page,
    }

    public long UsedByResource { get; set; }

    public UsageType Usage { get; set; }

    /// <summary>
    ///   Reference to the media. This uses a GUID as that is much cheaper to parse when trying to update things and
    ///   reduces database lookups.
    /// </summary>
    public Guid MediaFileGuid { get; set; }

    public MediaFile MediaFile { get; set; } = null!;
}
