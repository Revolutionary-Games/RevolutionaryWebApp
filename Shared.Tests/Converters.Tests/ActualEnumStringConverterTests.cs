namespace ThriveDevCenter.Shared.Tests.Converters.Tests
{
    using System.Runtime.Serialization;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Shared.Utilities;
    using Xunit;

    public class ActualEnumStringConverterTests
    {
        [Fact]
        public void JsonEnum_EnumMemberSerialize()
        {
            var serialized = JsonSerializer.Serialize(new Container
            {
                Value = SomeEnum.Value1
            });

            Assert.Equal(@"{""Value"":""Value1""}", serialized);

            serialized = JsonSerializer.Serialize(new Container
            {
                Value = SomeEnum.Second
            });

            Assert.Equal(@"{""Value"":""abc""}", serialized);

            serialized = JsonSerializer.Serialize(new Container
            {
                Value = SomeEnum.Third
            });

            Assert.Equal(@"{""Value"":""third""}", serialized);
        }

        [Theory]
        [InlineData("Value1", SomeEnum.Value1)]
        [InlineData("abc", SomeEnum.Second)]
        [InlineData("third", SomeEnum.Third)]
        [InlineData("value1", SomeEnum.Value1)]
        [InlineData("aBc", SomeEnum.Second)]
        [InlineData("THIRD", SomeEnum.Third)]
        public void JsonEnum_EnumMemberDeSerialize(string value, SomeEnum expected)
        {
            var deserialized = JsonSerializer.Deserialize<Container>($"{{\"Value\":\"{value}\"}}");
            Assert.NotNull(deserialized);
            Assert.Equal(expected, deserialized.Value);
        }

        [Theory]
        [InlineData(SomeEnum.Value1)]
        [InlineData(SomeEnum.Second)]
        [InlineData(SomeEnum.Third)]
        public void JsonEnum_EnumMemberRoundTrip(SomeEnum value)
        {
            var original = new Container
            {
                Value = value
            };
            var serialized = JsonSerializer.Serialize(original);

            var deserialized = JsonSerializer.Deserialize<Container>(serialized);

            Assert.NotNull(deserialized);

            Assert.Equal(value, deserialized.Value);
        }

        private class Container
        {
            public SomeEnum Value { get; set; }
        }

        [JsonConverter(typeof(ActualEnumStringConverter))]
        public enum SomeEnum
        {
            Value1,

            [EnumMember(Value = "abc")]
            Second,

            [EnumMember(Value = "third")]
            Third
        }
    }
}
