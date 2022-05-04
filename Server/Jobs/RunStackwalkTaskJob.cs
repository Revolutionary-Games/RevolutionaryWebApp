namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;

    [DisableConcurrentExecution(1000)]
    public class RunStackwalkTaskJob
    {
        private readonly ILogger<RunStackwalkTaskJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly ILocalTempFileLocks localTempFileLocks;
        private readonly IStackwalk stackwalk;
        private readonly IStackwalkSymbolPreparer symbolPreparer;
        private readonly string symbolFolder;

        public RunStackwalkTaskJob(ILogger<RunStackwalkTaskJob> logger, NotificationsEnabledDb database,
            IConfiguration configuration, ILocalTempFileLocks localTempFileLocks, IStackwalk stackwalk,
            IStackwalkSymbolPreparer symbolPreparer)
        {
            this.logger = logger;
            this.database = database;
            this.localTempFileLocks = localTempFileLocks;
            this.stackwalk = stackwalk;
            this.symbolPreparer = symbolPreparer;
            symbolFolder = configuration["Crashes:StackwalkSymbolFolder"];
        }

        public async Task Execute(Guid taskId, CancellationToken cancellationToken)
        {
            if (!stackwalk.Configured)
                throw new Exception("Stackwalk is not configured");

            var task = await database.StackwalkTasks.FindAsync(new object[] { taskId }, cancellationToken);

            if (task == null)
            {
                logger.LogError("Can't stackwalk on non-existent task: {TaskId}", taskId);
                return;
            }

            var symbolPrepareTask = symbolPreparer.PrepareSymbolsInFolder(symbolFolder, cancellationToken);

            logger.LogInformation("Starting stackwalk on task {TaskId}", taskId);

            var semaphore =
                localTempFileLocks.GetTempFilePath(task.DumpTempCategory, out string baseFolder);

            var filePath = Path.Combine(baseFolder, task.DumpFileName);

            FileStream? dump = null;

            // On Linux an open file should not impact deleting etc. so I'm pretty sure this is pretty safe
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                if (File.Exists(filePath))
                    dump = File.OpenRead(filePath);
            }
            finally
            {
                semaphore.Release();
            }

            await symbolPrepareTask;

            if (string.IsNullOrEmpty(task.DumpFileName) || dump == null)
            {
                logger.LogError("Can't stackwalk for task with missing dump file: {FilePath}", filePath);
                return;
            }

            var startTime = DateTime.UtcNow;

            // TODO: implement an async API in the stackwalk service and swap to using that here
            // TODO: also then combine this with StartStackwalkOnReportJob class
            string result;

            try
            {
                result = await stackwalk.PerformBlockingStackwalk(dump, task.StackwalkPlatform, cancellationToken);
                task.Succeeded = true;
            }
            catch (Exception e)
            {
                // TODO: probably wants to retry at least once or twice here instead of immediately failing
                logger.LogError(e, "Failed to run stackwalk task");
                result = "Failed to run stackwalk";
                task.Succeeded = false;
            }

            var duration = DateTime.UtcNow - startTime;
            logger.LogInformation("Stackwalking (task) took: {Duration}", duration);

            if (task.DeleteDumpAfterRunning)
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    File.Delete(filePath);
                    logger.LogInformation("Deleted processed file for stackwalk task: {FilePath}", filePath);
                }
                finally
                {
                    semaphore.Release();
                }
            }

            if (string.IsNullOrWhiteSpace(result))
                result = "Resulting decoded crash dump is empty";

            task.Result = result;
            task.FinishedAt = DateTime.UtcNow;

            // Don't want to cancel here as we can no longer undelete the file
            // ReSharper disable once MethodSupportsCancellation
            await database.SaveChangesAsync();
        }
    }
}
