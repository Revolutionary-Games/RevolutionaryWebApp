namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Text.Json;
using Server.Utilities;
using Xunit;

public class ModelUpdateApplyHelperTests
{
    [Fact]
    public void ModelUpdateApplyHelper_NoChangesResultsInNoChanges()
    {
        var old = new Model();
        var initial = JsonSerializer.Serialize(old);
        var update = new Model();

        Assert.Equal(JsonSerializer.Serialize(old), JsonSerializer.Serialize(update));

        var (changes, description, fields) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(old, update);

        Assert.False(changes);
        Assert.Null(description);
        Assert.Null(fields);
        Assert.Equal(JsonSerializer.Serialize(old), initial);
        Assert.Equal(update.SomeField, old.SomeField);
    }

    [Fact]
    public void ModelUpdateApplyHelper_BasicChangeApplies()
    {
        var old = new Model();
        var initial = JsonSerializer.Serialize(old);
        var update = new ModelDTO();

        var (changes, description, fields) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(old, update);

        Assert.True(changes);
        Assert.NotNull(description);
        Assert.NotEmpty(description);
        Assert.False(description.StartsWith(","));
        Assert.NotNull(fields);
        Assert.Contains(nameof(Model.SomeField), fields);
        Assert.DoesNotContain(nameof(Model.Id), fields);
        Assert.DoesNotContain(nameof(Model.Flag), fields);

        Assert.NotEqual(JsonSerializer.Serialize(old), initial);

        Assert.Contains(nameof(Model.SomeField), description);
        Assert.DoesNotContain(nameof(Model.Id), description);
        Assert.DoesNotContain(nameof(Model.Flag), description);
        Assert.Equal(update.SomeField, old.SomeField);
    }

    [Fact]
    public void ModelUpdateApplyHelper_IgnoredFieldsDoesNotApply()
    {
        var old = new Model();
        var initial = JsonSerializer.Serialize(old);
        var update = new ModelDTO
        {
            SomeField = old.SomeField,
            Id = 123,
        };

        var (changes, description, fields) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(old, update);

        Assert.False(changes);
        Assert.Null(description);
        Assert.Null(fields);
        Assert.Equal(JsonSerializer.Serialize(old), initial);
        Assert.NotEqual(update.Id, old.Id);
    }

    [Fact]
    public void ModelUpdateApplyHelper_MultipleFieldUpdate()
    {
        var old = new Model();
        var initial = JsonSerializer.Serialize(old);
        var update = new ModelDTO
        {
            Flag = true,
        };

        Assert.NotEqual(update.Flag, old.Flag);

        var (changes, description, fields) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(old, update);

        Assert.True(changes);
        Assert.NotNull(description);
        Assert.NotEmpty(description);
        Assert.NotEqual(JsonSerializer.Serialize(old), initial);
        Assert.Contains(nameof(Model.SomeField), description);
        Assert.DoesNotContain(nameof(Model.Id), description);
        Assert.Contains(nameof(Model.Flag), description);
        Assert.NotNull(fields);
        Assert.Contains(nameof(Model.SomeField), fields);
        Assert.DoesNotContain(nameof(Model.Id), fields);
        Assert.Contains(nameof(Model.Flag), fields);

        Assert.Equal(update.SomeField, old.SomeField);
        Assert.Equal(update.Flag, old.Flag);
    }

    [Fact]
    public void ModelUpdateApplyHelper_BadModelThrows()
    {
        var old = new BadModel();
        var update = new BadModel();

        Assert.Throws<ArgumentException>(() => ModelUpdateApplyHelper.ApplyUpdateRequestToModel(old, update));
    }

    private class Model
    {
        // ReSharper disable once PropertyCanBeMadeInitOnly.Local
        public long Id { get; set; } = 1;

        [UpdateFromClientRequest]
        public string SomeField { get; set; } = "A value";

        [UpdateFromClientRequest]
        public bool? Flag { get; set; }
    }

    private class ModelDTO
    {
        public long Id { get; set; }
        public string SomeField { get; set; } = "DTO";
        public bool? Flag { get; set; }
    }

    private class BadModel
    {
        public long Id { get; set; } = 1;

        public string SomeField { get; set; } = "A value";

        public bool? Flag { get; set; }
    }
}
