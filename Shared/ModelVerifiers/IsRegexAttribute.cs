namespace ThriveDevCenter.Shared.ModelVerifiers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

/// <summary>
///   Validates that property is a valid regex
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class IsRegexAttribute : RequiredAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // Allow null values
        if (ReferenceEquals(value, null))
            return ValidationResult.Success;

        var asString = value as string;

        if (asString == null)
        {
            throw new InvalidOperationException(
                $"Can't apply {nameof(IsRegexAttribute)} to a non-string type");
        }

        if (asString.Length < 1)
        {
            return new ValidationResult(
                ErrorMessage ??
                $"The {validationContext.DisplayName} field is blank (and not null) and as regex would match anything.",
                new[] { validationContext.MemberName! });
        }

        try
        {
            var _ = new Regex(asString);
        }
        catch (Exception)
        {
            return new ValidationResult(
                ErrorMessage ??
                $"The {validationContext.DisplayName} field must be a valid regex pattern.",
                new[] { validationContext.MemberName! });
        }

        return ValidationResult.Success;
    }
}
