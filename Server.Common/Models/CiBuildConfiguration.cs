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
        public Dictionary<string, CiJobConfiguration> Jobs { get; set; }
    }

    public class CiJobConfiguration
    {
        [Required]
        [StringLength(500, MinimumLength = 4)]
        public string Image { get; set; }

        public CiJobCacheConfiguration Cache { get; set; }

        [Required]
        [MinLength(1)]
        [MaxLength(50)]
        public List<CiJobBuildStep> Steps { get; set; }

        public CiArtifactsConfiguration Artifacts { get; set; } = new();
    }

    public class CiJobCacheConfiguration
    {
        [Required]
        [MinLength(1)]
        [MaxLength(10)]
        [JsonPropertyName("load_from")]
        public List<string> LoadFrom { get; set; }

        [JsonPropertyName("write_to")]
        public string WriteTo { get; set; }

        [MinLength(1)]
        [MaxLength(10)]
        [JsonPropertyName("shared")]
        public Dictionary<string, string> Shared { get; set; }

        [MinLength(1)]
        [MaxLength(5)]
        [JsonPropertyName("system")]
        public Dictionary<string, string> System { get; set; }
    }

    public class CiJobBuildStep
    {
        public CiJobBuildStepRun Run { get; set; }
    }

    public class CiJobBuildStepRun
    {
        [Required]
        [StringLength(90, MinimumLength = 2)]
        public string Name { get; set; }

        [Required]
        [StringLength(4000, MinimumLength = 1)]
        public string Command { get; set; }

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
        public List<string> Paths { get; set; } = new();
    }
}
