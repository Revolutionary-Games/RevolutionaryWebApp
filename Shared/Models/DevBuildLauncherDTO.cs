namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.Text.Json.Serialization;

    public class DevBuildLauncherDTO : ITimestampedModel
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("build_hash")]
        public string BuildHash { get; set; }

        [JsonPropertyName("platform")]
        public string Platform { get; set; }

        [JsonPropertyName("branch")]
        public string Branch { get; set; }

        [JsonPropertyName("build_zip_hash")]
        public string BuildZipHash { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

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
}
