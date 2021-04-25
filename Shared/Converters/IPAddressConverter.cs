namespace ThriveDevCenter.Shared.Converters
{
    using System;
    using System.Net;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class IPAddressConverter : JsonConverter<IPAddress>
    {
        public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();

            if (value == null)
                return null;

            return IPAddress.Parse(value);
        }

        public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
