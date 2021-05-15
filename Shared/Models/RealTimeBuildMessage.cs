namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.Text.Json.Serialization;
    using Notifications;
    using Utilities;

    public class RealTimeBuildMessage : SerializedNotification
    {
        [Required]
        [JsonConverter(typeof(ActualEnumStringConverter))]
        public BuildSectionMessageType Type { get; set; }

        public string Output { get; set; }

        [MaxLength(100)]
        public string SectionName { get; set; }

        public bool WasSuccessful { get; set; }
    }

    public enum BuildSectionMessageType
    {
        SectionStart,
        BuildOutput,
        SectionEnd,
        FinalStatus,
    }
}
