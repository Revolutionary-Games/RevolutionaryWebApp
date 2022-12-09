namespace ThriveDevCenter.Server.Tests.Services.Tests;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Server.Services;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class RegistrationStatusTests : IDisposable
{
    private const string Code = "superimportantcode";

    private readonly XunitLogger<RegistrationStatus> logger;

    public RegistrationStatusTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<RegistrationStatus>(output);
    }

    [Fact]
    public void ParseConfigurationWorks_NotEnabled()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string>("Registration:Enabled", "false"),
            new KeyValuePair<string, string>("Registration:RegistrationCode", Code),
        }).Build();

        var service = new RegistrationStatus(configuration, logger);

        Assert.False(service.RegistrationEnabled);
    }

    [Fact]
    public void ParseConfigurationWorks_CantEnableWithoutCode()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string>("Registration:Enabled", "true"),
        }).Build();

        var service = new RegistrationStatus(configuration, logger);

        Assert.False(service.RegistrationEnabled);
    }

    [Fact]
    public void ParseConfigurationWorks_CantEnableWithBlankCode()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string>("Registration:Enabled", "true"),
            new KeyValuePair<string, string>("Registration:RegistrationCode", string.Empty),
        }).Build();

        var service = new RegistrationStatus(configuration, logger);

        Assert.False(service.RegistrationEnabled);
    }

    [Fact]
    public void ParseConfigurationWorks_Enabled()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string>("Registration:Enabled", "true"),
            new KeyValuePair<string, string>("Registration:RegistrationCode", Code),
        }).Build();

        var service = new RegistrationStatus(configuration, logger);

        Assert.True(service.RegistrationEnabled);
    }

    [Fact]
    public void ParseConfigurationWorks_EnabledDifferentCapitals()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string>("Registration:Enabled", "True"),
            new KeyValuePair<string, string>("Registration:RegistrationCode", Code),
        }).Build();

        var service = new RegistrationStatus(configuration, logger);

        Assert.True(service.RegistrationEnabled);
    }

    [Fact]
    public void ParseConfigurationWorks_CodeIsRead()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string>("Registration:Enabled", "true"),
            new KeyValuePair<string, string>("Registration:RegistrationCode", Code),
        }).Build();

        var service = new RegistrationStatus(configuration, logger);

        Assert.True(service.RegistrationEnabled);
        Assert.Equal(Code, service.RegistrationCode);
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
