namespace RevolutionaryWebApp.Server.Tests.Authorization.Tests;

using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Server.Authorization;
using Server.Controllers;
using Shared.Models.Enums;
using Xunit;

public class APIKeyEndpointScopeTests
{
    [Theory]
    [InlineData("CIProject", "Get", APIKeyScope.ContinuousIntegration)]
    [InlineData("CIProject", "GetSingle", APIKeyScope.ContinuousIntegration)]
    [InlineData("CIProject", "GetJob", APIKeyScope.ContinuousIntegration)]
    [InlineData("CIProject", "CreateNew", APIKeyScope.CIConfiguration)]
    [InlineData("CIProject", "DeleteResource", APIKeyScope.CIConfiguration)]
    [InlineData("CIProject", "RestoreResource", APIKeyScope.CIConfiguration)]
    [InlineData("CISecrets", "Get", APIKeyScope.CIConfiguration)]
    [InlineData("CIBuildManagement", "ReRunBuild", APIKeyScope.ContinuousIntegration)]
    [InlineData("CrashReport", "GetSingle", APIKeyScope.Reports)]
    [InlineData("CrashReport", "ReProcessCrashDump", APIKeyScope.Tools)]
    [InlineData("ReleaseStats", "Get", APIKeyScope.PublicInfo)]
    [InlineData("ReleaseStats", "GetSingle", APIKeyScope.PublicInfo)]
    [InlineData("ReleaseStats", "GetConfiguration", APIKeyScope.Administration)]
    [InlineData("ReleaseStats", "Create", APIKeyScope.Administration)]
    public async Task RoutingUsesExpectedScope(string controller, string action, APIKeyScope expectedScope)
    {
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services => services.AddControllers().AddApplicationPart(typeof(CIProjectController)
                .Assembly))
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            })).StartAsync();

        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var endpoint = Assert.Single(endpoints, endpoint =>
        {
            var descriptor = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            return descriptor?.ControllerName == controller && descriptor.ActionName == action;
        });

        Assert.Equal(expectedScope, endpoint.Metadata.GetMetadata<APIKeyScopeAttribute>()?.Scope);
    }
}
