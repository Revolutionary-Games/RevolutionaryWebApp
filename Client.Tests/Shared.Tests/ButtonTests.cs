namespace RevolutionaryWebApp.Client.Tests.Shared.Tests;

using System;
using Bunit;
using Client.Shared;
using Xunit;

public class ButtonTests : BunitContext
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardsTooltipAndAccessibleLabel(bool hasCallback)
    {
        var button = Render<Button>(parameters =>
        {
            parameters.AddUnmatched("title", "Delete comment");
            parameters.AddUnmatched("aria-label", "Delete comment");
            if (hasCallback)
                parameters.Add(component => component.OnPressed, (Action)(() => { }));
        });

        var element = button.Find("button");
        Assert.Equal("Delete comment", element.GetAttribute("title"));
        Assert.Equal("Delete comment", element.GetAttribute("aria-label"));
    }
}
