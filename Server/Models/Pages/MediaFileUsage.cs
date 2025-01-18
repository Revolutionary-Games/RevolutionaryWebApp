namespace RevolutionaryWebApp.Server.Models.Pages;

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

    public long MediaFileId { get; set; }

    public MediaFile MediaFile { get; set; } = null!;

    public long UsedByResource { get; set; }

    public UsageType Usage { get; set; }
}
