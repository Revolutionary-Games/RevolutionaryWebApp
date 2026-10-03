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
}
