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

    // [Fact]
    public void VersionedPage_ReverseDiffGenerationConvertsToJson()
    {
        var diffReverse = DiffGenerator.Default.Generate(NewText1, OldText1);

        Assert.NotNull(diffReverse.Blocks);
        Assert.NotEmpty(diffReverse.Blocks);

        var serialized = JsonSerializer.Serialize(diffReverse, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // First check it didn't end up being empty
        Assert.NotEqual("{}", serialized);

        Assert.Equal("some text", serialized);

        // And verify deserialize gives back the same result
        var deserialized = JsonSerializer.Deserialize<DiffData>(serialized);
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Blocks);
        Assert.NotEmpty(deserialized.Blocks);

        Assert.Equal(diffReverse.Blocks[0], deserialized.Blocks[0]);
    }

    [Fact]
    public void VersionedPage_OldVersionCreationWorks()
    {
        // TODO: implement
    }

    [Fact]
    public void VersionedPage_RevertingOldVersionsWork()
    {
        // TODO: implement
    }
}
