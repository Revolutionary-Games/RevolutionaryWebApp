namespace RevolutionaryWebApp.Client.Tests.Mocks;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

/// <summary>
///   Mock helpers for bUnit HttpClient. From https://bunit.egilhansen.com/docs/test-doubles/mocking-httpclient.html
///   with changes.
/// </summary>
public static class MockHttpClientHelpers
{
    public static MockHttpMessageHandler AddMockHttpClient(this TestServiceProvider services)
    {
        var mockHttpHandler = new MockHttpMessageHandler();
        var httpClient = mockHttpHandler.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost:5000");
        services.AddSingleton(httpClient);
        return mockHttpHandler;
    }

    public static MockedRequest RespondJson<T>(this MockedRequest request, T content)
    {
        request.Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(content))
                {
                    Headers =
                    {
                        ContentType = new MediaTypeHeaderValue("application/json"),
                    },
                },
            };

            return response;
        });

        return request;
    }

    public static MockedRequest RespondJson<T>(this MockedRequest request, Func<T> contentProvider)
    {
        request.Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(contentProvider()))
                {
                    Headers =
                    {
                        ContentType = new MediaTypeHeaderValue("application/json"),
                    },
                },
            };

            return response;
        });

        return request;
    }
}
