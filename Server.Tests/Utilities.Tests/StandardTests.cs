namespace ThriveDevCenter.Server.Tests.Utilities.Tests
{
    using System;
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
    }
}
