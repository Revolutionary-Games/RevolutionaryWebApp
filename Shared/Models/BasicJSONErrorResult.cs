namespace RevolutionaryWebApp.Shared.Models;

using System.Text.Json;
using System.Text.Json.Serialization;

public class BasicJSONErrorResult
{
    public BasicJSONErrorResult(string error, string message)
    {
        Error = error;
        Message = message;
    }

    [JsonPropertyName("error")]
    public string Error { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}
