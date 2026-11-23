namespace RevolutionaryWebApp.Client;

using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RevolutionaryWebApp.Shared;
using RevolutionaryWebApp.Shared.Models;
using Services;
using Utilities;

/// <summary>
///   Retries a request once after the server explicitly rejects it for requiring sudo mode.
/// </summary>
public class SudoModeHandler(SudoModeService sudo) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var generation = sudo.Generation;
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Forbidden ||
            !response.Headers.TryGetValues(AppInfo.SudoModeResponseHeader, out var values) ||
            !values.Contains(AppInfo.SudoModeRequiredHeaderValue))
        {
            return response;
        }

        try
        {
            // Keep the error body readable by the caller when the user cancels the prompt.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var challenge = JsonSerializer.Deserialize<SudoModeRequiredDTO>(body,
                HttpClientHelpers.GetOptionsWithSerializers());
            if (challenge == null)
                return response;

            using var retry = await CloneRequest(request, cancellationToken);
            if (!await sudo.Request(challenge, generation).WaitAsync(cancellationToken))
                return response;

            cancellationToken.ThrowIfCancellationRequested();
            response.Dispose();

            // Sending through the inner handler prevents loops if sudo expires again or is revoked.
            return await base.SendAsync(retry, cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async Task<HttpRequestMessage> CloneRequest(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        foreach (var option in request.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);

        if (request.Content != null)
        {
            clone.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}
