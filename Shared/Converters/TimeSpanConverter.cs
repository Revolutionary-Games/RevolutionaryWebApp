namespace ThriveDevCenter.Shared.Converters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class TimeSpanConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TimeSpan.Parse(reader.GetString() ?? throw new JsonException("TimeSpan value is null"));
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
