namespace ThriveDevCenter.Shared.Tests.ModelVerifiers.Tests;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Shared.ModelVerifiers;
using Xunit;

public class NoTrailingOrPrecedingSpaceAttributeTests
{
    [Fact]
    public void NoTrailingWhitespace_AllowsValid()
    {
        var model = new Model1();

        var errors = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.Empty(errors);

        model.Property = "a";

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.Empty(errors);

        model.Property = "thing with spaces in the middle";

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.Empty(errors);
    }

    [Fact]
    public void NoTrailingWhitespace_DisallowsInvalid()
    {
        var model = new Model1();

        var errors = new List<ValidationResult>();

        model.Property = "thing ";

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.NotEmpty(errors);

        Assert.NotNull(errors[0].ErrorMessage);
        Assert.Contains(nameof(Model1.Property), errors[0].MemberNames);

        model.Property = " thing";

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));

        model.Property = "thing\t";

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
    }

    private class Model1
    {
        [NoTrailingOrPrecedingSpace]
        public string? Property { get; set; }
    }
}
