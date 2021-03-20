using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace ThriveDevCenter.Server
{
    using System;
    using System.Configuration;
    using System.Data;
    using Jobs;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Quartz;

    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); }).ConfigureServices(
                    (hostContext, services) =>
                    {
                        // I didn't get this to work properly...
                        // services.Configure<QuartzOptions>(hostContext.Configuration.GetSection("Quartz"));

                        // Setup background jobs
                        services.AddQuartz(q =>
                        {
                            q.SchedulerName = "ThriveDevCenter Quartz Scheduler";

                            // "Scoped" factory is used so that we can fetch scoped services in jobs
                            q.UseMicrosoftDependencyInjectionScopedJobFactory();

                            q.UseDefaultThreadPool(options =>
                            {
                                var concurrency = Convert.ToInt32(hostContext.Configuration["Quartz:ThreadCount"]);

                                if (concurrency < 1)
                                {
                                    throw new ConstraintException(
                                        "Quartz thread pool concurrency is out of range");
                                }

                                options.MaxConcurrency = concurrency;
                            });

                            q.UsePersistentStore(configure =>
                            {
                                // Same db used for the models and jobs, for now this is done for simplicity
                                configure.UsePostgres(options =>
                                {
                                    options.ConnectionString =
                                        hostContext.Configuration.GetConnectionString("WebApiConnection");
                                    options.TablePrefix = "qrtz_";
                                });

                                configure.UseJsonSerializer();
                                configure.UseProperties = true;
                            });

                            // Setup default jobs
                            q.AddJobAndTrigger<SessionCleanupJob>(hostContext.Configuration);
                        });

                        services.AddQuartzHostedService(options =>
                        {
                            // This is a bit bad as we have the quit seriously rate limited discourse reading jobs...
                            // those probably need to be re-designed
                            options.WaitForJobsToComplete = true;
                        });
                    });
    }
}
