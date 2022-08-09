namespace ThriveDevCenter.Server.Tests.Utilities.Tests;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Server.Utilities;
using Xunit;

public class SelectByHashedPropertyTests
{
    [Fact]
    public void DoubleHash_ConsistentHashForSameValue()
    {
        var guid = Guid.NewGuid();
        Assert.Equal(SelectByHashedProperty.DoubleHashAsIdStandIn(guid.ToString(), null),
            SelectByHashedProperty.DoubleHashAsIdStandIn(guid.ToString(), null));
    }

    [Fact]
    public void DoubleHash_WorksWithIntermediateValue()
    {
        var guid = Guid.NewGuid();

        var raw = guid.ToString();
        var single = SelectByHashedProperty.HashForDatabaseValue(raw);
        var doubleHash = Convert.ToBase64String(SHA256.HashData(Convert.FromBase64String(single)));

        Assert.Equal(Convert.ToBase64String(SHA256.HashData(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))),
            doubleHash);

        // For some reason this doesn't result in the right values, even though the roundtrip as bytes in a string
        // should work
        // Assert.Equal(doubleHash,
        //     SelectByHashedProperty.HashForDatabaseValue(Encoding.UTF8.GetString(Convert.FromBase64String(single))));

        var tempQuery = Convert.FromBase64String(doubleHash).Take(sizeof(long));

        if (BitConverter.IsLittleEndian)
            tempQuery = tempQuery.Reverse();

        var doubleLong = BitConverter.ToInt64(tempQuery.ToArray(), 0);

        Assert.Equal(SelectByHashedProperty.DoubleHashAsIdStandIn(raw, single),
            SelectByHashedProperty.DoubleHashAsIdStandIn(raw, null));
        Assert.Equal(doubleLong, SelectByHashedProperty.DoubleHashAsIdStandIn(raw, null));
        Assert.Equal(doubleLong, SelectByHashedProperty.DoubleHashAsIdStandIn(raw, single));
        Assert.Throws<ArgumentNullException>(() => SelectByHashedProperty.DoubleHashAsIdStandIn(null!, null));
        SelectByHashedProperty.DoubleHashAsIdStandIn(null!, single);
    }

    [Fact]
    public void DoubleHash_NoCollisionWithRandomValues()
    {
        // Just a sanity thing to make sure some values don't have collisions
        Assert.NotEqual(SelectByHashedProperty.DoubleHashAsIdStandIn(Guid.NewGuid().ToString(), null),
            SelectByHashedProperty.DoubleHashAsIdStandIn(Guid.NewGuid().ToString(), null));

        var testString = "JustARandomTextString";

        Assert.NotEqual(SelectByHashedProperty.DoubleHashAsIdStandIn(testString, null),
            SelectByHashedProperty.DoubleHashAsIdStandIn(testString + "a", null));
    }
}