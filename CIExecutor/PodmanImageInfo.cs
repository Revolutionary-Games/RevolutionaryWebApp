namespace CIExecutor;

using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
///   Parsed image info from podman JSON format
/// </summary>
public class PodmanImageInfo
{
    public string? Id { get; set; }
    public string? ParentId { get; set; }

    public long Size { get; set; }

    public int Containers { get; set; }

    /// <summary>
    ///   Numeric creation time from podman JSON. In many versions this is Unix epoch seconds.
    /// </summary>
    public long Created { get; set; }

    /// <summary>
    ///   Raw string creation time (podman uses formats like
    ///   "YYYY-MM-DD HH:MM:SS +0000 UTC" or ISO8601 "YYYY-MM-DDTHH:MM:SSZ").
    ///   Keep as string to parse robustly.
    /// </summary>
    [JsonPropertyName("CreatedAt")]
    public string? CreatedAtRaw { get; set; }

    /// <summary>
    ///   Some podman versions expose CreatedTime as an alias for CreatedAt.
    /// </summary>
    [JsonPropertyName("CreatedTime")]
    public string? CreatedTimeRaw { get; set; }

    public bool Dangling { get; set; }

    public string? Digest { get; set; }

    /// <summary>
    ///   This contains the names of the image
    /// </summary>
    public List<string> Names { get; set; } = new();

    public List<string> History { get; set; } = new();
}
