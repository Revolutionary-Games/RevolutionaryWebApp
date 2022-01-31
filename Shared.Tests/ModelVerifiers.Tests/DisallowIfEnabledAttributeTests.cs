namespace ThriveDevCenter.Shared.Tests.ModelVerifiers.Tests
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using Shared.ModelVerifiers;
    using Xunit;

    public class DisallowIfEnabledAttributeTests
    {
        [Fact]
        public void DisallowedValueIfAnotherPropertyMatches_StringEqualityFailsCorrectly()
        {
            var model = new Model1();

            var errors = new List<ValidationResult>();

            Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
            Assert.Empty(errors);

            model.PropertyOne = "something else";

            Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
            Assert.Empty(errors);

            model.PropertyOne = "value";

            Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
            Assert.Empty(errors);

            model.DependentProperty = "value";

            Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
            Assert.Empty(errors);

            model.DependentProperty = "disallow";

            Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
            Assert.NotEmpty(errors);

            Assert.NotNull(errors[0].ErrorMessage);
            Assert.Contains(nameof(Model1.DependentProperty), errors[0].MemberNames);

            model.PropertyOne = "another thing";

            Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        }

        [Fact]
        public void DisallowedValueIfAnotherPropertyMatches_EnumValue()
        {
            var model = new Model1
            {
                DependentProperty = "disallow",
                Flag = AnEnum.Value2,
            };

            var errors = new List<ValidationResult>();

            Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
            Assert.NotEmpty(errors);

            Assert.NotNull(errors[0].ErrorMessage);
            Assert.Contains(nameof(Model1.DependentProperty), errors[0].MemberNames);
        }

        [Fact]
        public void DisallowedValueIfAnotherPropertyMatches_EnumValueDoesNotTriggerWhenShouldNot()
        {
            var model = new Model1();

            var errors = new List<ValidationResult>();

            Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
            Assert.Empty(errors);

            model.DependentProperty = "disallow";

            Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
            Assert.Empty(errors);
        }

        private class Model1
        {
            public AnEnum Flag { get; set; } = AnEnum.Value1;

            public string? PropertyOne { get; set; }

            [DisallowIf(ThisMatches = "disallow", OtherProperty = nameof(PropertyOne), IfOtherMatchesValue = "value")]
            [DisallowIf(ThisMatches = "disallow", OtherProperty = nameof(Flag),
                IfOtherMatchesValue = nameof(AnEnum.Value2))]
            [DisallowIfEnabled]
            public string? DependentProperty { get; set; }
        }

        private enum AnEnum
        {
            Value1,
            Value2,
        }
    }
}
