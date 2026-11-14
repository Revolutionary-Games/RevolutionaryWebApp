namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Linq;
using System.Threading;
using Hangfire;
using Hangfire.Common;
using Server.Jobs;
using Xunit;

public class StackwalkJobQueueTests
{
    [Fact]
    public void RunStackwalkTaskJob_IsAssignedToStackwalkQueue()
    {
        var job = Job.FromExpression<RunStackwalkTaskJob>(x => x.Execute(Guid.Empty, CancellationToken.None));

        AssertUsesStackwalkQueue(job);
    }

    [Fact]
    public void StartStackwalkOnReportJob_IsAssignedToStackwalkQueue()
    {
        var job = Job.FromExpression<StartStackwalkOnReportJob>(x => x.Execute(0, CancellationToken.None));

        AssertUsesStackwalkQueue(job);
    }

    private static void AssertUsesStackwalkQueue(Job job)
    {
        var queueAttribute = JobFilterProviders.Providers.GetFilters(job)
            .Select(filter => filter.Instance)
            .OfType<QueueAttribute>()
            .SingleOrDefault();

        Assert.NotNull(queueAttribute);
        Assert.Equal("stackwalk", queueAttribute.Queue);
    }
}
