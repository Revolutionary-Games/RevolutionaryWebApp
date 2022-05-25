namespace ThriveDevCenter.Shared.ModelVerifiers;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   Requires that a property when Trim is called doesn't change (or the property is null)
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class NoTrailingOrPrecedingSpaceAttribute : RequiredAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // Allow null values
        if (ReferenceEquals(value, null))
            return ValidationResult.Success;

        var trimMethod = value.GetType().GetMethod(nameof(string.Trim), new Type[] { });

        if (trimMethod == null)
        {
            throw new InvalidOperationException(
                $"Can't apply {nameof(NoTrailingOrPrecedingSpaceAttribute)} to a type that has no Trim method");
        }

        var trimmed = trimMethod.Invoke(value, new object?[] { });

        if (trimmed == null)
        {
            throw new InvalidOperationException(
                $"Can't apply {nameof(NoTrailingOrPrecedingSpaceAttribute)} to a type that has null returning Trim method");
        }

        if (!trimmed.Equals(value))
        {
            return new ValidationResult(
                ErrorMessage ??
                $"The {validationContext.DisplayName} field must contain trailing or preceding whitespace.",
                new[] { validationContext.MemberName! });
        }

        return ValidationResult.Success;
    }
}
