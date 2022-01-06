namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.Text.Json.Serialization;

    public class CreateCrashReportResponse
    {
        [Required]
        [JsonPropertyName("createdId")]
        public long CreatedId { get; set; }

        [Required]
        [JsonPropertyName("deleteKey")]
        public string DeleteKey { get; set; }
    }
}
