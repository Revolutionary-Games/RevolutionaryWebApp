namespace ThriveDevCenter.Shared.Tests.ModelVerifiers.Tests;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Shared.ModelVerifiers;
using Xunit;

public class MustContainAttributeTests
{
    [Fact]
    public void MustContain_DoesNotAllowNull()
    {
        var model = new Model1();

        var errors = new List<ValidationResult>();

        model.Property = null;

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.NotEmpty(errors);

        Assert.NotNull(errors[0].ErrorMessage);
        Assert.Contains(nameof(Model1.Property), errors[0].MemberNames);
    }

    [Fact]
    public void MustContain_DoesNotAllowEmpty()
    {
        var model = new Model1();

        var errors = new List<ValidationResult>();

        model.Property = string.Empty;

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.NotEmpty(errors);

        Assert.NotNull(errors[0].ErrorMessage);
        Assert.Contains(nameof(Model1.Property), errors[0].MemberNames);
    }

    [Fact]
    public void MustContain_StringProperty()
    {
        var model = new Model1();

        var errors = new List<ValidationResult>();

        model.Property = "string with z";

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.Empty(errors);

        model.Property = "z";

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.Empty(errors);

        model.Property = "thing without that letter";

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.NotEmpty(errors);

        Assert.NotNull(errors[0].ErrorMessage);
        Assert.Contains(nameof(Model1.Property), errors[0].MemberNames);
    }

    [Fact]
    public void MustContain_ListOfStringsProperty()
    {
        var model = new Model2(new List<string>() { "item", "and other stuff", "third thing" });

        var errors = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.Empty(errors);

        model.Property = new List<string>() { "item" };

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.Empty(errors);

        model.Property = new List<string>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.NotEmpty(errors);

        Assert.NotNull(errors[0].ErrorMessage);
        Assert.Contains(nameof(Model2.Property), errors[0].MemberNames);
    }

    [Fact]
    public void MustContain_MultipleValues()
    {
        var model = new Model3();

        var errors = new List<ValidationResult>();

        model.Property = "string with z and b";

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.Empty(errors);

        model.Property = "only z";

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.NotEmpty(errors);

        Assert.NotNull(errors[0].ErrorMessage);
        Assert.Contains(nameof(Model3.Property), errors[0].MemberNames);

        model.Property = "only b";
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));

        model.Property = string.Empty;
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
    }

    private class Model1
    {
        [MustContain("z")]
        public string? Property { get; set; }
    }

    private class Model2
    {
        public Model2(List<string> property)
        {
            Property = property;
        }

        [MustContain("item")]
        public List<string> Property { get; set; }
    }

    private class Model3
    {
        [MustContain("z", "b")]
        public string? Property { get; set; }
    }
}
