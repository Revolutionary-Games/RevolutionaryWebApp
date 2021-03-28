namespace ThriveDevCenter.Server.ModelVerifiers
{
    using System;
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    ///   Requires that a property is not null when a condition is true (for example a boolean property is true).
    ///   Derives from RequiredAttribute to make sure this is always used in validations.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public class NotNullOrEmptyIfAttribute : RequiredAttribute
    {
        public string BooleanPropertyIsTrue { get; set; }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
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
            else
            {
                throw new InvalidOperationException("NotNullOrEmpty is misconfigured");
            }

            if (requiredValue)
            {
                return CheckRequired(value, validationContext.DisplayName);
            }

            return ValidationResult.Success;
        }

        private ValidationResult CheckRequired(object value, string propertyName)
        {
            if (value == null)
                return new ValidationResult($"Required property '{propertyName}' is null");

            if (value is string str)
            {
                if (string.IsNullOrWhiteSpace(str))
                    return new ValidationResult($"Required property '{propertyName}' is empty");
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
