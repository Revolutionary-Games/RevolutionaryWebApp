namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using Server.Services;
using Xunit;

public class ResxTranslationParserTests
{
    [Fact]
    public void ParsesStringResourcesAndPreservesSourceComments()
    {
        const string source = "<root><data name=\"Title\" xml:space=\"preserve\"><value>Launch</value>" +
            "<comment>Window title</comment></data><data name=\"Icon\" type=\"System.Drawing.Bitmap\">" +
            "<value>ignored</value></data></root>";
        const string target = "<root><data name=\"Title\"><value>Käynnistä</value></data></root>";

        var entry = Assert.Single(ResxTranslationParser.Parse(source, target));

        Assert.Equal("Title", entry.Key);
        Assert.Equal("Launch", entry.SourceText);
        Assert.Equal("Käynnistä", entry.TargetText);
        Assert.Equal("Window title", entry.Description);
    }
}
