namespace ThriveDevCenter.Server.Utilities;

using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public static class HttpRequestExtensions
{
    public static async Task<ReadResult> ReadBodyAsync(this HttpRequest request)
    {
        var reader = request.BodyReader;
        var readResult = await reader.ReadAsync();

        // This line is needed to suppress "System.InvalidOperationException: Reading is already in progress."
        // Though even this doesn't seem to always suppress all warnings...
        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
        return readResult;
    }
}
