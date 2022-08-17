namespace ThriveDevCenter.Shared.Tests.Utilities.Tests;

using System.Collections.Generic;
using System.Linq;
using Converters;
using Shared.Models.Enums;
using Xunit;

public class TerminalEscapeHandlingTest
{
    [Fact]
    public void TerminalEscape_HandlesSingleColourSection()
    {
        const string input =
            "[0;94;49mBeginning upload of 2 devbuilds with 2 dehydrated objects[0m";

        var result = TerminalEscapeHandling.HandleTerminalEscapes(input).ToList();
        var expected = new List<TerminalEscapeHandling.TextSection>
        {
            new()
            {
                Colour = TerminalColour.Blue,
                Text = "Beginning upload of 2 devbuilds with 2 dehydrated objects",
            },
        };

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected[0], result[0]);
    }
}
