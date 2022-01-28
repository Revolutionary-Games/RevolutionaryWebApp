namespace ThriveDevCenter.Shared.ModelVerifiers
{
    using System;
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    ///   Requires that a property is not null when a condition is true (for example a boolean property is true).
    ///   Derives from RequiredAttribute to make sure this is always used in validations.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     Multiple are currently not allowed because the other validations don't run at all. See:
    ///     https://stackoverflow.com/questions/40273835/asp-net-core-model-validation-multiple-attributes
    ///     for a solution that doesn't work
    ///   </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class NotNullOrEmptyIfAttribute : RequiredAttribute
    {
        public string? BooleanPropertyIsTrue { get; set; }

        public string? PropertyMatchesValue { get; set; }

        public string? Value { get; set; }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            bool requiredValue = false;

            if (!string.IsNullOrEmpty(BooleanPropertyIsTrue))
            {
                var property = validationContext.ObjectType.GetProperty(BooleanPropertyIsTrue);

                if (property == null)
                    throw new InvalidOperationException("NotNullOrEmpty target property to read is missing");

                var propertyValue = property.GetValue(validationContext.ObjectInstance);

                if (propertyValue != null)
                {
                    if ((bool)propertyValue)
                        requiredValue = true;
                }
            }
            else if (!string.IsNullOrEmpty(PropertyMatchesValue))
            {
                var property = validationContext.ObjectType.GetProperty(PropertyMatchesValue);

                if (property == null)
                    throw new InvalidOperationException("NotNullOrEmpty conditional property to read is missing");

                var propertyValue = property.GetValue(validationContext.ObjectInstance);

                if (ReferenceEquals(Value, propertyValue))
                {
                    requiredValue = true;
                }
                else if (propertyValue != null && Value != null)
                {
                    var converter = TypeDescriptor.GetConverter(property.PropertyType);
                    if (propertyValue.Equals(converter.ConvertFromInvariantString(Value)))
                        requiredValue = true;
                }
            }
            else
            {
                throw new InvalidOperationException("NotNullOrEmpty is misconfigured");
            }

            if (requiredValue)
                return CheckRequired(value, validationContext.MemberName ?? "unknown", validationContext.DisplayName);

            return ValidationResult.Success;
        }

        private ValidationResult? CheckRequired(object? value, string propertyName, string displayName)
        {
            if (value == null)
                return new ValidationResult($"The {displayName} field is required.", new[] { propertyName });

            if (value is string str)
            {
                if (string.IsNullOrWhiteSpace(str))
                {
                    return new ValidationResult($"The {displayName} field should not be empty.",
                        new[] { propertyName });
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"Value for property '{propertyName}' is an unsupported type for checking");
            }

            return ValidationResult.Success;
        }
    }
}
