namespace RevolutionaryWebApp.Shared.Tests.Utilities.Tests;

using Shared.Utilities;
using Xunit;

public class PathParserTests
{
    [Fact]
    public void PathParser_FileParentFolder()
    {
        Assert.Equal("/home/staging/tmp/backupWorkDir",
            PathParser.GetParentPath(
                "/home/staging/tmp/backupWorkDir/ThriveDevCenter-Backup_2022-02-13T17:05:39.2980431Z.tar.xz"));
    }

    [Fact]
    public void PathParser_EmptyParentPath()
    {
        Assert.Equal(string.Empty, PathParser.GetParentPath("/folder"));
        Assert.Equal(string.Empty, PathParser.GetParentPath("/"));
    }
}
