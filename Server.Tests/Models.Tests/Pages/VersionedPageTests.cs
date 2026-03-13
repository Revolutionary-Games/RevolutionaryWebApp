namespace RevolutionaryWebApp.Server.Tests.Models.Tests.Pages;

using System.Text.Json;
using SharedBase.Utilities;
using Xunit;

public class VersionedPageTests
{
    private const string OldText1 = """
                                    This is just some text
                                    with multiple lines
                                    that may be changed in the future
                                    to be something else
                                    """;

    private const string NewText1 = """
                                    This is just some text
                                    with multitude of lines
                                    that may be changed in the future
                                    and lines inserted
                                    as well as deleted
                                    """;

    [Fact]
    private void VersionedPage_ReverseDiffGenerationConvertsToJson()
    {
        var diffReverse = DiffGenerator.Default.Generate(NewText1, OldText1);

        Assert.NotNull(diffReverse.DiffDeltaRaw);

        var serialized = JsonSerializer.Serialize(diffReverse, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // First check it didn't end up being empty
        Assert.NotEqual("{}", serialized);

        // This forces the JSON format to not change at all, this should be a fine test as we'll store these in the
        // database for a really long time, so any difference is likely very terrible
        // ReSharper disable StringLiteralTypo
        Assert.Equal(
            "{\"diff\":\"=33\\t-3\\t\\u002Bpl\\t=2\\t-3\\t=40\\t-3\\t\\u002Bto\\t=1\\t-3\\t\\u002Bb\\t=1\\t-1\\t=1" +
            "\\t-2\\t=1\\t\\u002Bom\\t=1\\t-1\\t=1\\t-5\\t\\u002Bhing\\t=1\\t-1\\t=2\\t-3\\t=1\\t-6\\t=1\\t-1\"}",
            serialized);

        // ReSharper restore StringLiteralTypo

        // And verify deserialize gives back the same result
        var deserialized = JsonSerializer.Deserialize<DiffData>(serialized);
        Assert.NotNull(deserialized);
        Assert.Equal(diffReverse.DiffDeltaRaw, deserialized.DiffDeltaRaw);

        Assert.Equal(diffReverse.ToString(), deserialized.ToString());
    }
}
