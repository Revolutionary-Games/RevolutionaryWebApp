namespace ThriveDevCenter.Shared.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.Serialization;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class ActualEnumStringConverter : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsEnum;
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            JsonConverter converter = (JsonConverter)Activator.CreateInstance(
                typeof(ActualEnumConverter<>).MakeGenericType(typeToConvert),
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                new object?[] { options },
                culture: null)!;

            return converter;
        }
    }

    internal class ActualEnumConverter<T> : JsonConverter<T>
        where T : notnull
    {
        private readonly Dictionary<T, string> valueToStringMap = new();
        private readonly Dictionary<string, T> stringToValueMap = new();

        public ActualEnumConverter(JsonSerializerOptions options)
        {
            var type = typeof(T);
            var enumValues = type.GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (var value in enumValues)
            {
                var realValue = (T)value.GetValue(null)!;

                var attribute = value.GetCustomAttribute(typeof(EnumMemberAttribute)) as EnumMemberAttribute;

                var stringValue = attribute?.Value ?? value.Name;

                valueToStringMap[realValue!] = stringValue;
                stringToValueMap[stringValue.ToLowerInvariant()] = realValue;
            }
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var stringValue = reader.GetString();

            // TODO: allow null values?
            // if (stringValue == null)
            //    return null;

            if (stringValue == null)
                throw new JsonException("Enum string value is null");

            try
            {
                return stringToValueMap[stringValue.ToLowerInvariant()];
            }
            catch (KeyNotFoundException)
            {
                throw new JsonException("Invalid given value for enum type");
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(valueToStringMap[value]);
        }
    }
}
