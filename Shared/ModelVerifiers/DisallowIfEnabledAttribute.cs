namespace ThriveDevCenter.Shared.ModelVerifiers
{
    using System;
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Reflection;

    /// <summary>
    ///   Requires that a property doesn't equal a value if another property has a matching value to the given
    ///   condition
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class DisallowIfEnabledAttribute : RequiredAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (string.IsNullOrEmpty(validationContext.MemberName))
                throw new InvalidOperationException("MemberName is null");

            // We need to guess here that this is applied to a property
            var members = validationContext.ObjectInstance.GetType().GetMember(validationContext.MemberName);

            if (members.Length != 1)
                throw new InvalidOperationException("Currently validation member not found by name");

            var valueProperty = members[0].GetCustomAttributes<DisallowIfAttribute>(true).ToList();

            if (valueProperty.Count < 1)
                throw new InvalidOperationException("No configuration attributes for DisallowIf found");

            foreach (var configuration in valueProperty)
            {
                if (string.IsNullOrEmpty(configuration.OtherProperty))
                    throw new InvalidOperationException("DisallowIf is misconfigured");

                // Skip if the current property this attribute is on doesn't match the required value
                bool matches = false;

                if (ReferenceEquals(value, configuration.ThisMatches))
                {
                    matches = true;
                }
                else if (value != null)
                {
                    var converter = TypeDescriptor.GetConverter(value.GetType());
                    matches = value.Equals(converter.ConvertFromInvariantString(configuration.ThisMatches));
                }

                if (!matches)
                    return ValidationResult.Success;

                var property = validationContext.ObjectType.GetProperty(configuration.OtherProperty);

                if (property == null)
                {
                    throw new InvalidOperationException(
                        "DisallowedValueIfAnotherPropertyMatches OtherProperty to read is missing");
                }

                var propertyValue = property.GetValue(validationContext.ObjectInstance);

                bool fail = false;

                if (ReferenceEquals(configuration.IfOtherMatchesValue, propertyValue))
                {
                    fail = true;
                }
                else if (propertyValue != null && configuration.IfOtherMatchesValue != null)
                {
                    var converter = TypeDescriptor.GetConverter(property.PropertyType);
                    fail = propertyValue.Equals(
                        converter.ConvertFromInvariantString(configuration.IfOtherMatchesValue));
                }

                if (fail)
                {
                    // We can't return multiple error messages so for now just return the first
                    return new ValidationResult(
                        configuration.ErrorMessage ?? ErrorMessage ??
                        $"The {validationContext.DisplayName} field should not equal {value} " +
                        $"when {configuration.OtherProperty} equals {configuration.IfOtherMatchesValue}.",
                        new[] { validationContext.MemberName });
                }
            }

            return ValidationResult.Success;
        }
    }

    /// <summary>
    ///   Configures a single condition checked by <see cref="DisallowIfEnabledAttribute"/>
    ///   Without these that attribute doesn't work. This is needed to workaround problems with running multiple
    ///   validations attributes with the same class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public class DisallowIfAttribute : Attribute
    {
        public string ThisMatches { get; set; }

        public string OtherProperty { get; set; }

        public string IfOtherMatchesValue { get; set; }

        public string ErrorMessage { get; set; }
    }
}
