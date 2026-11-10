namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
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

    [Theory]
    [InlineData("", "")]
    [InlineData("#, fuzzy\n", "")]
    public void GettextExportDoesNotMarkEmptyTranslationsFuzzy(string flags, string expectedFlags)
    {
        const string entry = "msgid \"EMPTY\"\nmsgstr \"\"\n";
        var value = new TranslationValue
        {
            Key = "EMPTY", Text = string.Empty, State = TranslationState.NeedsTranslation,
        };

        Assert.Equal(expectedFlags + entry,
            TranslationFileExporter.ExportGettext(entry, flags + entry, [value]));
    }

    [Theory]
    [InlineData("#, fuzzy\n", "")]
    [InlineData("#, fuzzy\n#,\n", "")]
    [InlineData("#, c-format, fuzzy\n", "#, c-format\n")]
    public void GettextExportRemovesEmptyFlagLines(string flags, string expectedFlags)
    {
        const string entry = "msgid \"HELLO\"\nmsgstr \"Hei\"\n";
        var value = new TranslationValue
        {
            Key = "HELLO", Text = "Hei", State = TranslationState.Verified,
        };

        Assert.Equal(expectedFlags + entry,
            TranslationFileExporter.ExportGettext(entry, flags + entry, [value]));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void GettextExportPreservesHeaderCommentsWrappingAndLineEndings(string newline)
    {
        var target = ("# Translations template for Thrive.\n# Copyright (C) 2026 Revolutionary Games Studio\n" +
                "# FIRST AUTHOR <EMAIL@ADDRESS>, 2026.\n#\nmsgid \"\"\nmsgstr \"\"\n" +
                "\"Content-Type: text/plain; charset=UTF-8\\n\"\n\n" +
                "#. Greeting\nmsgid \"HELLO\"\nmsgstr \"Hei\"\n\n" +
                "msgid \"UNCHANGED\"\nmsgstr \"\"\n\"Hyvää \"\n\"päivää\"\n")
            .Replace("\n", newline);
        var value = new TranslationValue
        {
            Key = "HELLO", Text = "Terve", State = TranslationState.Translated,
        };

        Assert.Equal(target.Replace("Hei", "Terve"),
            TranslationFileExporter.ExportGettext(target, target, [value]));
    }

    [Fact]
    public void GettextExportPreservesObsoleteTranslationsAndTheirComments()
    {
        const string active = "msgid \"HELLO\"\nmsgstr \"Hei\"\n";
        const string obsolete = "\n# Keep old translation\n#, fuzzy\n" +
            "#~ msgid \"PASSED_FULFILLED_CONDITION\"\n#~ msgstr \"ja\"\n" +
            "\n#~ msgctxt \"old context\"\n#~ msgid \"HELLO\"\n#~ msgstr \"Old hello\"\n";
        var value = new TranslationValue
        {
            Key = "HELLO", Text = "Terve", State = TranslationState.Translated,
        };

        Assert.Equal(active.Replace("Hei", "Terve") + obsolete,
            TranslationFileExporter.ExportGettext(active, active + obsolete, [value]));
    }

    [Fact]
    public void GettextExportChangesMultilineContextTranslationAndKeepsOtherPluralForms()
    {
        const string target = "msgctxt \"menu\"\nmsgid \"ONE\"\nmsgid_plural \"MANY\"\n" +
            "msgstr[0] \"One\"\nmsgstr[1] \"Many\"\n";
        var value = new TranslationValue
        {
            Key = "menu\u0004ONE", Text = "First line\nSecond \"line\"", State = TranslationState.Translated,
        };

        var output = TranslationFileExporter.ExportGettext(target, target, [value]);
        var entry = Assert.Single(GettextTranslationParser.Parse(target, target, output));
        Assert.Equal(value.Text, entry.TargetText);
        Assert.Contains("msgstr[1] \"Many\"", output);
        Assert.Equal(output, TranslationFileExporter.ExportGettext(target, output, [value]));
    }

    [Fact]
    public void GettextExportAddsMissingTranslationWithoutRegeneratingExistingEntries()
    {
        const string template = "msgid \"NEW\"\nmsgstr \"\"\n";
        const string target = "# Keep formatting\nmsgid \"EXISTING\"\nmsgstr \"Old\"\n";
        var value = new TranslationValue
        {
            Key = "NEW", Text = "New text", State = TranslationState.NeedsTranslation,
        };

        var output = TranslationFileExporter.ExportGettext(template, target, [value]);
        Assert.Equal(target + "\n#, fuzzy\nmsgid \"NEW\"\nmsgstr \"New text\"\n", output);
        Assert.Equal(output, TranslationFileExporter.ExportGettext(template, output, [value]));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void GettextExportUpdatesOnlyRevisionHeaderWithLatestChangeTimestamp(string newline)
    {
        var target = ("# Header comment\nmsgid \"\"\nmsgstr \"\"\n" +
            "\"Language: fi\\n\"\n" +
            "\"PO-Revision-Date: 2020-01-02 03:04+0200\\n\"\n" +
            "\"Last-Translator: Henri Hyyryläinen\\n\"\n" +
            "\"Content-Type: text/plain; charset=UTF-8\\n\"\n\n" +
            "msgid \"HELLO\"\nmsgstr \"Hei\"\n").Replace("\n", newline);
        var value = new TranslationValue
        {
            Key = "HELLO", Text = "Terve", State = TranslationState.Translated,
        };
        var latestChange = new DateTime(2026, 11, 10, 14, 35, 42, DateTimeKind.Utc);

        var output = TranslationFileExporter.ExportGettext(target, target, [value], latestChange);
        Assert.Equal(target.Replace("Hei", "Terve")
            .Replace("2020-01-02 03:04+0200", "2026-11-10 14:35+0000"), output);
        Assert.Equal(output, TranslationFileExporter.ExportGettext(target, output, [value], latestChange.AddDays(1)));
    }

    [Fact]
    public void GettextExportAddsMissingRevisionHeaderAfterExistingHeaders()
    {
        const string target = "msgid \"\"\nmsgstr \"\"\n\"Language: fi\\n\"\n\n" +
            "msgid \"HELLO\"\nmsgstr \"Hei\"\n";
        var value = new TranslationValue
        {
            Key = "HELLO", Text = "Terve", State = TranslationState.Translated,
        };

        var output = TranslationFileExporter.ExportGettext(target, target, [value],
            new DateTime(2026, 11, 10, 14, 35, 0, DateTimeKind.Utc));
        Assert.Equal(target.Replace("Hei", "Terve").Replace("\"Language: fi\\n\"",
            "\"Language: fi\\n\"\n\"PO-Revision-Date: 2026-11-10 14:35+0000\\n\""), output);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void GettextExportUpdatesOnlyLastTranslatorAndPreservesHeaderOrder(string newline)
    {
        var target = ("# Header comment\nmsgid \"\"\nmsgstr \"\"\n" +
            "\"Language: fi\\n\"\n\"Last-Translator: Old translator <old@example.com>\\n\"\n" +
            "\"PO-Revision-Date: 2020-01-02 03:04+0200\\n\"\n" +
            "\"Content-Type: text/plain; charset=UTF-8\\n\"\n\n" +
            "msgid \"HELLO\"\nmsgstr \"Hei\"\n").Replace("\n", newline);
        var value = new TranslationValue
        {
            Key = "HELLO", Text = "Terve", State = TranslationState.Translated,
        };

        var output = TranslationFileExporter.ExportGettext(target, target, [value],
            lastTranslator: "New \"translator\" <noreply+1@example.com>");
        Assert.Equal(target.Replace("Hei", "Terve").Replace("Old translator <old@example.com>",
            """New \"translator\" <noreply+1@example.com>"""), output);
    }

    [Fact]
    public void GettextExportAddsMissingLastTranslatorHeader()
    {
        const string target = "msgid \"\"\nmsgstr \"\"\n\"Language: fi\\n\"\n\n" +
            "msgid \"HELLO\"\nmsgstr \"Hei\"\n";
        var value = new TranslationValue
        {
            Key = "HELLO", Text = "Terve", State = TranslationState.Translated,
        };

        var output = TranslationFileExporter.ExportGettext(target, target, [value],
            lastTranslator: "Translator <noreply+1@example.com>");
        Assert.Equal(target.Replace("Hei", "Terve").Replace("\"Language: fi\\n\"",
            "\"Language: fi\\n\"\n\"Last-Translator: Translator <noreply+1@example.com>\\n\""),
            output);
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
