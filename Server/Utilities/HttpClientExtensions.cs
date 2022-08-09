namespace ThriveDevCenter.Server.Utilities;

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///   Required to make proper requests to discourse APIs. From: https://stackoverflow.com/a/41958685
///   with modifications
/// </summary>
public static class HttpClientExtensions
{
    public static Task<HttpResponseMessage> DeleteAsJsonAsync<T>(this HttpClient httpClient, string requestUri,
        T data)
        where T : class
    {
        return httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, requestUri)
            { Content = Serialize(data) });
    }

    public static Task<HttpResponseMessage> DeleteAsJsonAsync<T>(this HttpClient httpClient, string requestUri,
        T data, CancellationToken cancellationToken)
        where T : class
    {
        return httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, requestUri) { Content = Serialize(data) },
            cancellationToken);
    }

    public static Task<HttpResponseMessage> DeleteAsJsonAsync<T>(this HttpClient httpClient, Uri requestUri, T data)
        where T : class
    {
        return httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, requestUri)
            { Content = Serialize(data) });
    }

    public static Task<HttpResponseMessage> DeleteAsJsonAsync<T>(this HttpClient httpClient, Uri requestUri, T data,
        CancellationToken cancellationToken)
        where T : class
    {
        return httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, requestUri) { Content = Serialize(data) },
            cancellationToken);
    }

    private static HttpContent Serialize(object data) =>
        new StringContent(JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Encoding.UTF8, "application/json");
}