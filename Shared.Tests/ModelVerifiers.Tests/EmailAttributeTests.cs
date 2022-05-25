namespace ThriveDevCenter.Shared.Tests.ModelVerifiers.Tests;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Shared.ModelVerifiers;
using Xunit;

public class EmailAttributeTests
{
    [Theory]
    [InlineData("test@example.com")]
    [InlineData("a@b.com")]
    [InlineData("a@b")]
    [InlineData(null)]
    public void Email_AllowsValid(string email)
    {
        var model = new Model1()
        {
            Email = email,
        };

        var errors = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("test@")]
    [InlineData("test@example.com ")]
    [InlineData("@example.com")]
    [InlineData("")]
    public void Email_DisallowsInvalid(string email)
    {
        var model = new Model1()
        {
            Email = email,
        };

        var errors = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.NotEmpty(errors);

        Assert.NotNull(errors[0].ErrorMessage);
        Assert.Contains(nameof(Model1.Email), errors[0].MemberNames);
    }

    [Fact]
    public void Email_DisallowsLong()
    {
        var model = new Model1();

        var errors = new List<ValidationResult>();

        var builder = new StringBuilder(AppInfo.MaxEmailLength);

        for (int i = 0; i < AppInfo.MaxEmailLength - 50; ++i)
        {
            builder.Append('a');
        }

        builder.Append("@example.com");

        model.Email = builder.ToString();

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.Empty(errors);

        builder.Clear();

        for (int i = 0; i < AppInfo.MaxEmailLength + 1; ++i)
        {
            builder.Append('a');
        }

        builder.Append("@example.com");

        model.Email = builder.ToString();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.NotEmpty(errors);

        Assert.NotNull(errors[0].ErrorMessage);
        Assert.Contains(nameof(Model1.Email), errors[0].MemberNames);
    }

    private class Model1
    {
        [Email]
        public string? Email { get; set; }
    }
}
