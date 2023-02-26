namespace ThriveDevCenter.Server.Tests.Utilities.Tests;

using System;
using System.Globalization;
using System.Text;
using Xunit;

/// <summary>
///   Tests that certain standard library operations perform as intended
/// </summary>
public class StandardTests
{
    private static readonly byte[] TestBytes = { 45, 60, 52, 50 };

    [Fact]
    public void StringEncoding_GetBytesRoundtrip()
    {
        Assert.Equal(TestBytes, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(TestBytes)));
    }

    [Fact]
    public void StringEncoding_Base64Roundtrip()
    {
        var single = Convert.ToBase64String(TestBytes);

        Assert.Equal(single, Convert.ToBase64String(Convert.FromBase64String(single)));

        Assert.Equal(single,
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Convert.FromBase64String(single)))));

        Assert.Equal(TestBytes, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Convert.FromBase64String(single))));
    }

    [Fact]
    public void DateTime_ParsingHasRightYear()
    {
        var parsed = DateTime.Parse("2027-09-29T12:40:49Z", CultureInfo.InvariantCulture).ToUniversalTime();

        Assert.Equal(new DateTime(2027, 09, 29, 12, 40, 49, DateTimeKind.Utc), parsed);

        parsed = DateTime.Parse("2022-04-30T17:10:02Z").ToUniversalTime();

        Assert.Equal(new DateTime(2022, 04, 30, 17, 10, 02, DateTimeKind.Utc), parsed);

        var parsed2 = DateTime.Parse("2022-04-30 17:10:02+0").ToUniversalTime();

        Assert.Equal(parsed, parsed2);
    }
}
