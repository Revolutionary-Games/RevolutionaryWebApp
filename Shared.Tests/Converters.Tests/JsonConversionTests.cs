namespace ThriveDevCenter.Shared.Tests.Converters.Tests;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Xunit;

public class JsonConversionTests
{
    [Fact]
    public void JsonConverter_RequiredStringEmptyFails()
    {
        var errors = new List<ValidationResult>();

        var original1 = new Model()
        {
            Data = "Some data",
        };

        var received1 = JsonSerializer.Deserialize<Model>(JsonSerializer.Serialize(original1));
        Assert.NotNull(received1);
        Assert.Equal(original1.Data, received1.Data);

        Assert.True(Validator.TryValidateObject(received1, new ValidationContext(received1), errors));
        Assert.Empty(errors);

        var original2 = new Model();

        var received2 = JsonSerializer.Deserialize<Model>(JsonSerializer.Serialize(original2));
        Assert.NotNull(received2);
        Assert.Equal(original2.Data, received2.Data);

        Assert.False(Validator.TryValidateObject(received2, new ValidationContext(received2), errors));
        Assert.NotEmpty(errors);

        errors.Clear();

        var received3 = JsonSerializer.Deserialize<Model>("{}");
        Assert.NotNull(received3);

        Assert.False(Validator.TryValidateObject(received3, new ValidationContext(received3), errors));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void JsonConverter_ReadMissingFieldWithInitializer()
    {
        var deserialized = JsonSerializer.Deserialize<WorseModel>("{}");
        Assert.NotNull(deserialized);

        // This shouldn't be null in the optimal world, but that is how this performs now
        Assert.Null(deserialized.Data);

        // At least validation fails
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(deserialized, new ValidationContext(deserialized), errors));
        Assert.NotEmpty(errors);
        errors.Clear();

        var deserialized2 = JsonSerializer.Deserialize<WorstModel>("{}");
        Assert.NotNull(deserialized2);

        Assert.Null(deserialized2.Data);

        Assert.False(Validator.TryValidateObject(deserialized2, new ValidationContext(deserialized2), errors));
        Assert.NotEmpty(errors);
        errors.Clear();
    }

    public class Model
    {
        [Required]
        public string Data { get; set; } = string.Empty;
    }

    public class WorseModel
    {
        [Required]
#pragma warning disable CS8618 //intentionally done for testing purposes
        public string Data { get; set; }
#pragma warning restore CS8618
    }

    public class WorstModel
    {
        [Required]
#pragma warning disable CS8618 //intentionally done for testing purposes
        public SubObject Data { get; set; }
#pragma warning restore CS8618

        public class SubObject
        {
        }
    }
}
