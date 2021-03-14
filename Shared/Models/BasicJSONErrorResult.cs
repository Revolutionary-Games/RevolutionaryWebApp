namespace ThriveDevCenter.Shared.Models
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class BasicJSONErrorResult
    {
        [JsonPropertyName("error")]
        public string Error { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        public BasicJSONErrorResult(string error, string message)
        {
            Error = error;
            Message = message;
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}
