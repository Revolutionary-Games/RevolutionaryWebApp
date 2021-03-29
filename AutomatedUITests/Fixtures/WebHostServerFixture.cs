namespace AutomatedUITests.Fixtures
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.ExceptionServices;
    using System.Threading;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Hosting.Server;
    using Microsoft.AspNetCore.Hosting.Server.Features;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Utilities;

    /// <summary>
    ///   Runs the application in Testing environment for use in unit tests.
    ///   From: https://www.meziantou.net/automated-ui-tests-an-asp-net-core-application-with-playwright-and-xunit.htm
    ///   with modifications
    /// </summary>
    public abstract class WebHostServerFixture : IDisposable
    {
        private readonly Lazy<Uri> rootUriInitializer;

        public Uri RootUri => rootUriInitializer.Value;
        public IHost Host { get; set; }

        public RealIntegrationTestDatabaseFixture DatabaseFixture { get; }

        public WebHostServerFixture()
        {
            DatabaseFixture = new RealIntegrationTestDatabaseFixture();

            rootUriInitializer = new Lazy<Uri>(() => new Uri(StartAndGetRootUri()));
        }

        protected virtual string StartAndGetRootUri()
        {
            // As the port is generated automatically, we can use IServerAddressesFeature to get the actual server URL
            Host = CreateWebHost();
            RunInBackgroundThread(Host.Start);
            return Host.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()
                .Addresses.Single();
        }

        public virtual void Dispose()
        {
            DatabaseFixture.Dispose();

            // Originally StopAsync was called after dispose
            Host?.StopAsync();
            Host?.Dispose();
        }

        protected abstract IHost CreateWebHost();

        private static void RunInBackgroundThread(Action action)
        {
            var isDone = new ManualResetEvent(false);

            ExceptionDispatchInfo edi = null;
            new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    edi = ExceptionDispatchInfo.Capture(ex);
                }

                isDone.Set();
            }).Start();

            if (!isDone.WaitOne(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Timed out waiting for: " + action);

            if (edi != null)
                throw edi.SourceException;
        }
    }

    public class WebHostServerFixture<TStartup> : WebHostServerFixture
        where TStartup : class
    {
        protected override IHost CreateWebHost()
        {
            var connection = RealIntegrationTestDatabaseFixture.GetConnectionString();

            // Looks like for some reason we need to manually build all the appsettings loading here
            var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Testing.json")
                .AddInMemoryCollection(new List<KeyValuePair<string, string>>
                {
                    new("ConnectionStrings:WebApiConnection", connection)
                })
                .Build();

            var solutionFolder = SolutionRootFolderFinder.FindSolutionRootFolder();

            return new HostBuilder()
                .ConfigureWebHost(webHostBuilder => webHostBuilder
                    .UseEnvironment("Development")
                    .UseConfiguration(configuration)
                    .UseKestrel()
                    .UseContentRoot(Path.GetFullPath(Path.Join(solutionFolder, "Client/wwwroot")))
                    .UseWebRoot(Path.GetFullPath(Path.Join(solutionFolder, "Client/wwwroot")))
                    .UseStaticWebAssets()
                    .UseStartup<TStartup>()

                    // TODO: the actual server should detect BaseUrl automatically, as it is now hardcoded to be this
                    // in appsettings.json. Or alternatively we could "naively" pick an empty port here and pass the url
                    // here and also in the configuration above
                    // .UseUrls($"http://localhost:5001"))
                    .UseUrls("http://127.0.0.1:0")) // :0 allows to choose a port automatically
                .Build();
        }
    }
}
