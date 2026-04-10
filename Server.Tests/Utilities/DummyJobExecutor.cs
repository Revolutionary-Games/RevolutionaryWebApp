namespace RevolutionaryWebApp.Server.Tests.Utilities;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Models;
using Common.Services;
using Shared.Models;

public class DummyJobExecutor : IJobExecutor
{
    private readonly bool willSucceed;
    private readonly List<ExampleSection> sections;
    private readonly List<CIJobDTO> jobsThisCanRun;

    public DummyJobExecutor(bool willSucceed, List<ExampleSection> sections, List<CIJobDTO> jobsThisCanRun)
    {
        this.willSucceed = willSucceed;
        this.sections = sections;
        this.jobsThisCanRun = jobsThisCanRun;
    }

    public async Task<bool> ExecuteJobAsync(CiJobCacheConfigurationEnriched cacheConfiguration, CIJobDTO jobDTO,
        IRunnerClientDataService dataService, IJobOutputForwarder jobOutput, IExecutorCache cache,
        CancellationToken cancellationToken)
    {
        bool canRun = false;

        await jobOutput.OnNewJobStarted();

        // Check that the runner "knows" about this job before pretending to run it
        foreach (var potential in jobsThisCanRun)
        {
            if (potential.CiProjectId == jobDTO.CiProjectId && potential.CiBuildId == jobDTO.CiBuildId &&
                potential.CiJobId == jobDTO.CiJobId)
            {
                if (potential.State is not CIJobState.Starting and not CIJobState.WaitingForServer)
                    continue;

                canRun = true;
                potential.State = CIJobState.Running;
                break;
            }
        }

        if (!canRun)
        {
            await jobOutput.OpenNewSection("Job info");
            await jobOutput.ForwardOutputToActiveSection("Unknown job to run!");
            await jobOutput.CloseSection(false);
            return false;
        }

        foreach (var section in sections)
        {
            await jobOutput.OpenNewSection(section.Name);
            await jobOutput.ForwardOutputToActiveSection(section.Text.Replace("JOB_ID", jobDTO.CiJobId.ToString()));
            await jobOutput.CloseSection(section.Success);
        }

        return willSucceed;
    }

    public IEnumerable<CIJobDTO> GetRunJobs()
    {
        foreach (var job in jobsThisCanRun)
        {
            if (job.State is CIJobState.Running or CIJobState.Finished)
                yield return job;
        }
    }

    public record ExampleSection(string Name, string Text, bool Success);
}
