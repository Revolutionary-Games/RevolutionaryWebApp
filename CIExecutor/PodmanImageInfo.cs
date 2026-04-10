namespace CIExecutor;

using System;
using System.Collections.Generic;

/// <summary>
///   Parsed image info from podman JSON format
/// </summary>
public class PodmanImageInfo
{
    public string? Id { get; set; }
    public string? ParentId { get; set; }

    public long Size { get; set; }

    public int Containers { get; set; }

    public long Created { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool Dangling { get; set; }

    public string? Digest { get; set; }

    /// <summary>
    ///   This contains the names of the image
    /// </summary>
    public List<string> Names { get; set; } = new();

    public List<string> History { get; set; } = new();
}
