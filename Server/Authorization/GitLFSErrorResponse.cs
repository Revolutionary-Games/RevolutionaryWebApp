namespace ThriveDevCenter.Server.Authorization;

using System.Text.Json;
using System.Text.Json.Serialization;

public class GitLFSErrorResponse
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("documentation_url")]
    public string DocumentationUrl { get; set; } = "https://wiki.revolutionarygamesstudio.com/wiki/Git_LFS";

    [JsonPropertyName("request_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestId { get; set; }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}
