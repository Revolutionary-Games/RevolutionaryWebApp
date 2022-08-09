namespace ThriveDevCenter.Server.Tests.Controllers.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Dummies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

public class ModelValidationTests
{
    [Fact]
    public async Task ModelValidation_IsPerformedForDummy()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddControllers();
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
                    });
            })
            .StartAsync();

        var response = await host.GetTestClient().PostAsJsonAsync("/dummy", new DummyController.DummyModel()
        {
            Field = "stuff",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        response = await host.GetTestClient().PostAsJsonAsync("/dummy", new DummyController.DummyModel());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        response = await host.GetTestClient().PostAsJsonAsync("/dummy", new DummyController.DummyModel()
        {
            Field = "stuff",
            AnotherField = "stuff",
            AValue = 4,
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        response = await host.GetTestClient().PostAsJsonAsync("/dummy", new DummyController.DummyModel()
        {
            Field = "stuff",
            AnotherField = "stuff",
            AValue = 5,
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        response = await host.GetTestClient().PostAsJsonAsync("/dummy", new DummyController.DummyModel()
        {
            Field = "stuff",
            AnotherField = "",
            AValue = 5,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(nameof(DummyController.DummyModel.AnotherField), body);
        Assert.DoesNotContain(nameof(DummyController.DummyModel.AValue), body);
    }
}