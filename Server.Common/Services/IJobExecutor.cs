namespace RevolutionaryWebApp.Server.Common.Services;

using System.Threading;
using System.Threading.Tasks;
using Models;
using Shared.Models;

/// <summary>
///   Interface between the main part of job runners and the code actually spawning the job process and setting up
///   the cache etc.
/// </summary>
public interface IJobExecutor
{
    /// <summary>
    ///   Sets up the folders and caches for a job and then runs it.
    /// </summary>
    /// <param name="cacheConfiguration">Cache configuration for the job</param>
    /// <param name="jobDTO">Job data to execute</param>
    /// <param name="dataService">Service for accessing runner client data</param>
    /// <param name="jobOutput">Where to send job output (must be flushed by the caller)</param>
    /// <param name="cache">Cache to ask where to put temporary folders</param>
    /// <param name="cancellationToken">Early run cancellation, there's an absolute 2-hour-limit on top of this</param>
    /// <returns>True if job execution was successful, false otherwise</returns>
    public Task<bool> ExecuteJobAsync(CiJobCacheConfiguration cacheConfiguration, CIJobDTO jobDTO,
        IRunnerClientDataService dataService, IJobOutputForwarder jobOutput, IExecutorCache cache,
        CancellationToken cancellationToken);
}
