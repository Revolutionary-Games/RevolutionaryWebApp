namespace ThriveDevCenter.Shared.ModelVerifiers;

using System;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   Requires that a property is not null and contains the specified item
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class MustContainAttribute : RequiredAttribute
{
    public MustContainAttribute(params string[] values)
    {
        Values = values;
    }

    public string[] Values { get; }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (ReferenceEquals(value, null))
        {
            return new ValidationResult(
                ErrorMessage ??
                $"The {validationContext.DisplayName} field should not be null.",
                new[] { validationContext.MemberName! });
        }

        foreach (var valueToCheck in Values)
        {
            if (string.IsNullOrEmpty(valueToCheck))
            {
                throw new InvalidOperationException(
                    $"{nameof(MustContainAttribute)} is configured wrong with an empty value to check");
            }

            if (value is string valueString)
            {
                if (!valueString.Contains(valueToCheck))
                {
                    return new ValidationResult(
                        ErrorMessage ??
                        $"The {validationContext.DisplayName} field must contain {valueToCheck}.",
                        new[] { validationContext.MemberName! });
                }
            }
            else if (value is IEnumerable valueEnumerable)
            {
                bool found = false;
                foreach (var existingValue in valueEnumerable)
                {
                    if (existingValue is string asString)
                    {
                        if (valueToCheck.Equals(asString))
                        {
                            found = true;
                            break;
                        }

                        continue;
                    }

                    var converter = TypeDescriptor.GetConverter(existingValue.GetType());
                    if (valueToCheck.Equals(converter.ConvertFromInvariantString(existingValue.ToString() ??
                            throw new InvalidOperationException(
                                "Value in an enumerable can't be converted to a string"))))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return new ValidationResult(
                        ErrorMessage ??
                        $"The {validationContext.DisplayName} field must contain {valueToCheck}.",
                        new[] { validationContext.MemberName! });
                }
            }
            else
            {
                return new ValidationResult(
                    ErrorMessage ??
                    $"The {validationContext.DisplayName} field is of unknown type to check that it contains a required value.",
                    new[] { validationContext.MemberName! });
            }
        }

        return ValidationResult.Success;
    }
}
