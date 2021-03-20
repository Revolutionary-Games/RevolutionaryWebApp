namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using Microsoft.Extensions.Configuration;
    using Quartz;

    public static class ServiceCollectionQuartzConfiguratorExtensions
    {
        /// <summary>
        ///   Add a job type with appsettings.json configured cron interval
        /// </summary>
        /// <param name="quartz">Quarts configuration to operate on</param>
        /// <param name="config">Config to load application configuration from</param>
        /// <typeparam name="T">The job type</typeparam>
        /// <exception cref="Exception">If invalid configuration</exception>
        public static void AddJobAndTrigger<T>(this IServiceCollectionQuartzConfigurator quartz,
            IConfiguration config)
            where T : IJob
        {
            var name = typeof(T).Name;

            // Load cron interval configuration from the appsettings.json
            var configurationKey = $"Quartz:CronJobs:{name}";
            var cronSchedule = config[configurationKey];

            // Some minor validation
            if (string.IsNullOrEmpty(cronSchedule))
            {
                throw new Exception(
                    $"Missing cron interval entry: {configurationKey} for a job");
            }

            var jobKey = new JobKey(name);
            quartz.AddJob<T>(opts => opts.WithIdentity(jobKey));

            quartz.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithIdentity(name + "-trigger")
                .WithCronSchedule(cronSchedule));
        }
    }
}
