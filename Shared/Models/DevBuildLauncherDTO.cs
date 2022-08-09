namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

// TODO: rename as v1 and implement v2 launcher API for the C# rewritten launcher
public class DevBuildLauncherDTO : ITimestampedModel
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [Required]
    [JsonPropertyName("build_hash")]
    public string BuildHash { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("branch")]
    public string Branch { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("build_zip_hash")]
    public string BuildZipHash { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("important")]
    public bool Important { get; set; }

    [JsonPropertyName("keep")]
    public bool Keep { get; set; }

    [JsonPropertyName("build_of_the_day")]
    public bool BuildOfTheDay { get; set; }

    [JsonPropertyName("anonymous")]
    public bool Anonymous { get; set; }

    [JsonPropertyName("verified")]
    public bool Verified { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}