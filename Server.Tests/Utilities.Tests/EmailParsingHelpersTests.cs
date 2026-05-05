namespace RevolutionaryWebApp.Server.Tests.Utilities.Tests;

using RevolutionaryWebApp.Server.Utilities;
using Xunit;

public class EmailParsingHelpersTests
{
    [Theory]
    [InlineData("rfc822; test@calico.k12.ar.us", "test@calico.k12.ar.us")]
    [InlineData("rfc822;test@calico.k12.ar.us", "test@calico.k12.ar.us")]
    [InlineData("<test@calico.k12.ar.us>", "test@calico.k12.ar.us")]
    [InlineData("\"User Name\" <user@example.com>", "user@example.com")]
    [InlineData("user@example.com,other@example.com", "user@example.com")]
    public void ExtractEmailFromDsn_ParsesCommonFormats(string raw, string expected)
    {
        var actual = EmailParsingHelpers.ExtractEmailFromDsn(raw);
        Assert.Equal(expected, actual);
    }
}
