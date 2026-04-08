namespace RevolutionaryWebApp.Server.Tests.Runner.Tests;

using System.Threading.Tasks;
using Common.Services;
using Common.Utilities;
using Xunit;

public class RunnerTextHandlingTests
{
    [Fact]
    public async Task Runner_TextOutputIsCutAlongSpecialLines()
    {
        using var receiver = new OutputGatherer();

        using var cutter = new TextToSectionCutAdapter(receiver);

        // Simulate a process sending lines
        await cutter.OnNewJobStarted();

        Assert.Empty(receiver.Sections);

        await cutter.OnProcessOutputLine(TextToSectionCutAdapter.OutputSpecialCommandMarker +
            " SectionStart This is an example section");
        Assert.True(receiver.HasOpenSection);
        Assert.NotNull(receiver.OpenSection);
        await cutter.OnProcessOutputLine("This is a regular line of output");
        await cutter.OnProcessOutputLine("Another regular line of output");
        await cutter.OnProcessOutputLine(TextToSectionCutAdapter.OutputSpecialCommandMarker + " SectionEnd 0");

        Assert.False(receiver.HasOpenSection);
        Assert.Null(receiver.OpenSection);

        var data = receiver.Sections;

        Assert.Single(data);

        var lines = data[0].Text.ToString().Split('\n');
        Assert.Equal("This is a regular line of output", lines[0]);
        Assert.Equal("Another regular line of output", lines[1]);
        Assert.True(data[0].Closed);
        Assert.True(data[0].Success);
        Assert.Equal(1, data[0].Id);
        Assert.Equal("This is an example section", data[0].Name);
    }
}
