namespace ThriveDevCenter.Server.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text;
    using System.Text.Json;

    public static class ModelUpdateApplyHelper
    {
        public static (bool changes, string? changeDescription, List<string>? changedFields)
            ApplyUpdateRequestToModel<T, TRequest>(T model, TRequest updateRequest)
            where T : class
            where TRequest : class
        {
            var changedFields = new List<string>();
            var stringBuilder = new StringBuilder(200);
            bool changes = false;

            var modelType = model.GetType();
            var requestType = updateRequest.GetType();

            bool foundAttribute = false;

            foreach (var property in modelType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var attribute = property.GetCustomAttribute<UpdateFromClientRequestAttribute>();

                if (attribute == null)
                    continue;

                foundAttribute = true;

                var requestName = attribute.RequestPropertyName ?? property.Name;

                var requestProperty = requestType.GetProperty(requestName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (requestProperty == null)
                {
                    throw new Exception(
                        $"Invalid property ({requestName}) specified that doesn't exist in request model");
                }

                var oldValue = property.GetValue(model);
                var newValue = requestProperty.GetValue(updateRequest);

                if (oldValue == newValue)
                    continue;

                if (oldValue != null && newValue != null && oldValue.Equals(newValue))
                    continue;

                changedFields.Add(property.Name);

                if (changes)
                    stringBuilder.Append(", ");

                stringBuilder.Append("changed \"");
                stringBuilder.Append(property.Name);
                stringBuilder.Append("\" from: ");
                stringBuilder.Append(JsonSerializer.Serialize(oldValue));
                stringBuilder.Append(" to new value: ");
                stringBuilder.Append(JsonSerializer.Serialize(newValue));

                changes = true;

                property.SetValue(model, newValue);
            }

            if (!foundAttribute)
            {
                throw new ArgumentException("Model has no attributes marked as updateable");
            }

            if (!changes)
                return (false, null, null);

            return (true, stringBuilder.ToString(), changedFields);
        }
    }
}
