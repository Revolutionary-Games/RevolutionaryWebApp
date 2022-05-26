namespace ThriveDevCenter.Shared.Tests.ModelVerifiers.Tests;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Shared.ModelVerifiers;
using Xunit;

public class IsRegexAttributeTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("just a thing")]
    [InlineData("some (regex)+stuff\\s here.*")]
    [InlineData(null)]
    public void IsRegex_AllowsValid(string email)
    {
        var model = new Model1()
        {
            Regex = email,
        };

        var errors = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("(")]
    [InlineData("[ab")]
    [InlineData("\\")]
    [InlineData("")]
    public void IsRegex_DisallowsInvalid(string email)
    {
        var model = new Model1()
        {
            Regex = email,
        };

        var errors = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.NotEmpty(errors);

        Assert.NotNull(errors[0].ErrorMessage);
        Assert.Contains(nameof(Model1.Regex), errors[0].MemberNames);
    }
    
    private class Model1
    {
        [IsRegex]
        public string? Regex { get; set; }
    }
}
