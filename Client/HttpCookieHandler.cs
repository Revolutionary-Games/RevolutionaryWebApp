namespace RevolutionaryWebApp.Client;

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

/// <summary>
///   Configures HttpClient to actually send cookies
/// </summary>
public class HttpCookieHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.SameOrigin);
        return base.SendAsync(request, cancellationToken);
    }
}
