namespace ThriveDevCenter.Server.Common.Models
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Text.Json.Serialization;

    /// <summary>
    ///   Build configuration read from a yaml file before starting build jobs
    /// </summary>
    public class CiBuildConfiguration
    {
        [Required]
        [Range(1, 1)]
        public int Version { get; set; }

        [MaxLength(20)]
        [MinLength(1)]
        [Required]

        // ReSharper disable once CollectionNeverUpdated.Global
        public Dictionary<string, CiJobConfiguration> Jobs { get; set; } = new();
    }

    public class CiJobConfiguration
    {
        [Required]
        [StringLength(500, MinimumLength = 4)]
        public string Image { get; set; } = string.Empty;

        [Required]
        public CiJobCacheConfiguration Cache { get; set; } = new();

        [Required]
        [MinLength(1)]
        [MaxLength(50)]

        // ReSharper disable once CollectionNeverUpdated.Global
        public List<CiJobBuildStep> Steps { get; set; } = new();

        public CiArtifactsConfiguration Artifacts { get; set; } = new();
    }

    public class CiJobCacheConfiguration
    {
        [Required]
        [MinLength(1)]
        [MaxLength(10)]
        [JsonPropertyName("load_from")]

        // ReSharper disable once CollectionNeverUpdated.Global
        public List<string> LoadFrom { get; set; } = new();

        [Required]
        [JsonPropertyName("write_to")]
        [StringLength(120, MinimumLength = 2)]
        public string WriteTo { get; set; } = string.Empty;

        [MinLength(1)]
        [MaxLength(10)]
        [JsonPropertyName("shared")]
        public Dictionary<string, string>? Shared { get; set; }

        [MinLength(1)]
        [MaxLength(5)]
        [JsonPropertyName("system")]
        public Dictionary<string, string>? System { get; set; }
    }

    public class CiJobBuildStep
    {
        public CiJobBuildStepRun? Run { get; set; }
    }

    public class CiJobBuildStepRun
    {
        [Required]
        [StringLength(90, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(4000, MinimumLength = 1)]
        public string Command { get; set; } = string.Empty;

        public CiJobStepRunCondition When { get; set; } = CiJobStepRunCondition.Success;
    }

    public enum CiJobStepRunCondition
    {
        Success,
        Failure,
        Always
    }

    public class CiArtifactsConfiguration
    {
        [MaxLength(25)]

        // ReSharper disable once CollectionNeverUpdated.Global
        public List<string> Paths { get; set; } = new();
    }
}
