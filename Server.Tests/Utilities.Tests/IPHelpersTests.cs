namespace ThriveDevCenter.Server.Tests.Utilities.Tests;

using System.Net;
using Server.Utilities;
using Xunit;

public class IPHelpersTests
{
    [Theory]
    [InlineData("127.0.0.1", "127.0.0.0")]
    [InlineData("168.1.2.3", "168.1.0.0")]
    [InlineData("168.221.2.55", "168.221.0.0")]
    [InlineData("::1", "0000:0000:0000::0")]
    [InlineData("2001:0db8:0000:0000:0000:ff00:0042:8329", "2001:0db8:0000::0")]
    [InlineData("2001:0db8:ab12:cd34:ef56:ff12:0042:8329", "2001:0db8:ab12::0")]
    public void IPHelpers_AnonymizationWorks(string rawAddress, string anonymizedVersion)
    {
        var ip = IPAddress.Parse(rawAddress);

        Assert.Equal(anonymizedVersion, IPHelpers.PartlyAnonymizedIP(ip));

        // Ensure that the anonymized version is still a valid address
        IPAddress.Parse(IPHelpers.PartlyAnonymizedIP(ip));
    }
}
