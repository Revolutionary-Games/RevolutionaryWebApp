namespace ThriveDevCenter.Shared.Notifications
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    ///   Base class for handling json serialization of notifications
    /// </summary>
    public abstract class SerializedNotification
    {
        [JsonIgnore]
        public string NotificationType => GetType().Name;
    }

    /// <summary>
    ///   Custom converter for the notifications. Note for this to work you need to cast to SerializedNotification
    ///   first before serializing
    /// </summary>
    /// <remarks>
    ///   <para>
    ///      This works by wrapping the given notification object in one more level of object that contains the type.
    ///   </para>
    /// </remarks>
    public class NotificationJsonConverter : JsonConverter<SerializedNotification>
    {
        private const string TypeKey = "$NotificationType";
        private const string InnerKeyName = "Notification";

        /// <summary>
        ///   Holds the list of types that this converter handles
        /// </summary>
        private readonly List<Type> types;

        public NotificationJsonConverter()
        {
            var type = typeof(SerializedNotification);

            // Could maybe use an attribute but for now anything that inherits SerializedNotification is fair game
            types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .Where(p => type.IsAssignableFrom(p) && p.IsClass && !p.IsAbstract).ToList();
        }

        public override SerializedNotification Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            // In Thrive this style of code just returns null
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            reader.Read();
            var propertyName = reader.GetString();
            if (propertyName == null || propertyName != TypeKey)
                throw new JsonException("expected notification type");

            reader.Read();
            var typeName = reader.GetString();
            if (string.IsNullOrEmpty(typeName))
                throw new JsonException("notification type name is empty or missing");

            reader.Read();
            var innerKey = reader.GetString();
            if (innerKey == null || innerKey != InnerKeyName)
                throw new JsonException("inner notification key is wrong");

            // Get the actual type to deserialize
            var type = types.FirstOrDefault(x => x.Name == typeName);
            if (type == null)
                throw new JsonException("Unknown NotificationType: " + typeName);

            // Prepare to read and then read the nested notification data
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            var result = (SerializedNotification)JsonSerializer.Deserialize(ref reader, type, options);

            // Read the object end
            reader.Read();
            if (reader.TokenType != JsonTokenType.EndObject)
                throw new JsonException();

            return result;
        }

        public override void Write(Utf8JsonWriter writer, SerializedNotification value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WriteString(TypeKey, value.NotificationType);

            writer.WritePropertyName(InnerKeyName);

            JsonSerializer.Serialize(writer, (object)value, options);

            writer.WriteEndObject();
        }
    }
}
