namespace RevolutionaryWebApp.Shared.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DevCenterCommunication.Models;
using SharedBase.Utilities;

public class PageVersionDTO : IIdentifiable
{
    public long PageId { get; set; }

    public int Version { get; set; }

    [MaxLength(AppInfo.MaxPageEditCommentLength)]
    public string? EditComment { get; set; }

    public bool Deleted { get; set; }

    [StringLength(AppInfo.MaxPageLength + GlobalConstants.KIBIBYTE * 32)]
    [Required]
    public string ReverseDiffRaw { get; set; } = string.Empty;

    [StringLength(AppInfo.MaxPageLength + GlobalConstants.KIBIBYTE * 32)]
    [Required]
    public string PageContentAtVersion { get; set; } = string.Empty;

    public long? EditedById { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    ///   This fake ID generation assumes that there aren't that many different pages and not all bits of the version
    ///   are used.
    /// </summary>
    public long Id => PageId | ((long)Version << 38);

    public static DiffData DecodeDiffData(string raw)
    {
        return JsonSerializer.Deserialize<DiffData>(raw,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new NullDecodedJsonException();
    }

    public DiffData DecodeDiffData()
    {
        return JsonSerializer.Deserialize<DiffData>(ReverseDiffRaw,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new NullDecodedJsonException();
    }
}
