namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System.Linq;
using Server.Models.Translations;
using Server.Services;
using Shared.Models;
using Xunit;

public class TranslationFileExporterTests
{
    [Fact]
    public void GettextExportRoundTripsFuzzyAndKeepsUnchangedFile()
    {
        const string template = "msgid \"HELLO\"\nmsgstr \"\"\n";
        const string english = "msgid \"HELLO\"\nmsgstr \"Hello\"\n";
        const string target = "msgid \"HELLO\"\nmsgstr \"Hej\"\n";
        var value = new TranslationValue
        {
            Key = "HELLO", Text = "Hej", State = TranslationState.Translated,
        };

        Assert.Equal(target, TranslationFileExporter.ExportGettext(template, target, [value]));

        value.Text = "Hallå";
        value.State = TranslationState.NeedsTranslation;
        var output = TranslationFileExporter.ExportGettext(template, target, [value]);
        var entry = Assert.Single(GettextTranslationParser.Parse(template, english, output));
        Assert.Equal("Hallå", entry.TargetText);
        Assert.True(entry.Fuzzy);

        value.State = TranslationState.Verified;
        var verified = TranslationFileExporter.ExportGettext(template, output, [value]);
        Assert.False(Assert.Single(GettextTranslationParser.Parse(template, english, verified)).Fuzzy);
    }

    [Fact]
    public void ResxExportAddsMissingKeyAndPreservesOtherResources()
    {
        const string source = "<root><data name=\"Greeting\"><value>Hello</value></data>" +
            "<data name=\"Farewell\"><value>Bye</value></data></root>";
        const string target = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<root><data name=\"Greeting\"><value>Hei</value></data></root>\n";
        var values = new[]
        {
            new TranslationValue { Key = "Greeting", Text = "Hei" },
            new TranslationValue { Key = "Farewell", Text = "Näkemiin" },
        };

        var output = TranslationFileExporter.ExportResx(source, target, values);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n", output);
        Assert.EndsWith("\n", output);
        var entries = ResxTranslationParser.Parse(source, output);
        Assert.Equal("Hei", entries.Single(entry => entry.Key == "Greeting").TargetText);
        Assert.Equal("Näkemiin", entries.Single(entry => entry.Key == "Farewell").TargetText);
        Assert.Equal(output, TranslationFileExporter.ExportResx(source, output, values));
    }

    [Fact]
    public void ResxExportChangesOnlyExistingValueWithoutAddingDeclarationWhitespace()
    {
        const string target = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<root>\n  <!-- Keep this comment -->\n  <data name=\"Greeting\" xml:space=\"preserve\">\n" +
            "    <value>Hei</value>\n    <comment>A greeting</comment>\n  </data>\n</root>\n";
        var values = new[] { new TranslationValue { Key = "Greeting", Text = "Hyvää päivää" } };

        Assert.Equal(target.Replace("Hei", "Hyvää päivää"),
            TranslationFileExporter.ExportResx(target, target, values));
    }

    [Theory]
    [InlineData("\n", "  ")]
    [InlineData("\r\n", "  ")]
    [InlineData("\n", "\t")]
    public void ResxExportFormatsMultipleNewEntriesLikeExistingEntries(string newline, string indent)
    {
        const string source = "<root><data name=\"Greeting\"><value>Hello</value></data>" +
            "<data name=\"Farewell\"><value>Bye</value></data>" +
            "<data name=\"Multiline\"><value>Two lines</value></data></root>";
        var target = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + newline +
            "<root>" + newline + indent + "<data name=\"Greeting\" xml:space=\"preserve\">" + newline +
            indent + indent + "<value>Hei</value>" + newline + indent + "</data>" + newline + "</root>" + newline;
        var values = new[]
        {
            new TranslationValue { Key = "Greeting", Text = "Hei" },
            new TranslationValue { Key = "Farewell", Text = "Näkemiin & hei" },
            new TranslationValue { Key = "Multiline", Text = "First line\nSecond line" },
        };
        var additions = indent + "<data name=\"Farewell\" xml:space=\"preserve\">" + newline +
            indent + indent + "<value>Näkemiin &amp; hei</value>" + newline + indent + "</data>" + newline +
            indent + "<data name=\"Multiline\" xml:space=\"preserve\">" + newline +
            indent + indent + "<value>First line" + newline + "Second line</value>" + newline +
            indent + "</data>" + newline;

        var output = TranslationFileExporter.ExportResx(source, target, values);

        Assert.Equal(target.Replace("</root>", additions + "</root>"), output);
        Assert.Equal("First line\nSecond line",
            ResxTranslationParser.Parse(source, output).Single(entry => entry.Key == "Multiline").TargetText);
        Assert.Equal(output, TranslationFileExporter.ExportResx(source, output, values));
    }

    [Fact]
    public void GettextAndResxSourceCatalogsExportEditedSourceText()
    {
        const string template = "msgid \"HELLO\"\nmsgstr \"\"\n";
        const string english = "msgid \"HELLO\"\nmsgstr \"Hello\"\n";
        const string sourceResx = "<root><data name=\"Greeting\"><value>Hello</value></data></root>";
        var gettextValue = new TranslationValue
        {
            Key = "HELLO", Text = "Hello there", State = TranslationState.Translated,
        };
        var resxValue = new TranslationValue
        {
            Key = "Greeting", Text = "Hello there", State = TranslationState.Translated,
        };

        var englishOutput = TranslationFileExporter.ExportGettext(template, english, [gettextValue]);
        var editedEnglish = Assert.Single(GettextTranslationParser.Parse(template, englishOutput, englishOutput));
        Assert.Equal("Hello there", editedEnglish.SourceText);

        var resxOutput = TranslationFileExporter.ExportResx(sourceResx, sourceResx, [resxValue]);
        var editedResx = Assert.Single(ResxTranslationParser.Parse(resxOutput, resxOutput));
        Assert.Equal("Hello there", editedResx.SourceText);
    }
}
