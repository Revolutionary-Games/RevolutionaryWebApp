namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System.Linq;
using Server.Services;
using Xunit;

public class GettextTranslationParserTests
{
    [Theory]
    [InlineData("[thrive:compound type=\\\"atp\\\"]ATP'yi[/thrive:compound], " +
        "[thrive:compound type=\\\"glucose\\\"]glukoza[/thrive:compound].",
        "[thrive:compound type=\"atp\"]ATP'yi[/thrive:compound], " +
        "[thrive:compound type=\"glucose\"]glukoza[/thrive:compound].")]
    [InlineData("[thrive:compound type=\\\"glucose\\\"]glukozu[/thrive:compound] " +
        "[thrive:compound type=\\\\\\\"oxygen\\\\\\\"]oksijen[/thrive:compound]",
        "[thrive:compound type=\"glucose\"]glukozu[/thrive:compound] " +
        "[thrive:compound type=\\\"oxygen\\\"]oksijen[/thrive:compound]")]
    public void ImportsQuotedBbcode(string encoded, string expected)
    {
        var catalog = "msgid \"PROCESS\"\nmsgstr \"" + encoded + "\"\n";
        var entry = Assert.Single(GettextTranslationParser.Parse(catalog, catalog, catalog));

        Assert.Equal(expected, entry.SourceText);
        Assert.Equal(expected, entry.TargetText);
    }

    [Fact]
    public void SourceReferencesComeFromTemplateWhenLocaleDoesNotContainThem()
    {
        const string template = "#: ../src/Main.cs:42\nmsgid \"HELLO\"\nmsgstr \"\"\n";
        const string english = "msgid \"HELLO\"\nmsgstr \"Hello\"\n";
        const string finnish = "msgid \"HELLO\"\nmsgstr \"Hei\"\n";

        var entry = Assert.Single(GettextTranslationParser.Parse(template, english, finnish));

        Assert.Equal("HELLO", entry.Key);
        Assert.Equal("Hello", entry.SourceText);
        Assert.Equal("Hei", entry.TargetText);
        Assert.Contains(entry.SourceLocations, location => location.Contains("Main.cs:42"));
    }

    [Fact]
    public void FuzzyTargetAndMissingTargetAreRecognized()
    {
        const string template = "msgid \"ONE\"\nmsgstr \"\"\n\nmsgid \"TWO\"\nmsgstr \"\"\n";
        const string english = "msgid \"ONE\"\nmsgstr \"One\"\n\nmsgid \"TWO\"\nmsgstr \"Two\"\n";
        const string target = "#, fuzzy\nmsgid \"ONE\"\nmsgstr \"Uno\"\n";

        var entries = GettextTranslationParser.Parse(template, english, target);

        Assert.True(entries.Single(e => e.Key == "ONE").Fuzzy);
        Assert.Equal(string.Empty, entries.Single(e => e.Key == "TWO").TargetText);
    }
}
