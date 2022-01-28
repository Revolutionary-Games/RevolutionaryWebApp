namespace ThriveDevCenter.Shared.Tests.ModelVerifiers.Tests
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using Shared.ModelVerifiers;
    using Xunit;

    public class NotNullOrEmptyIfAttributeTest
    {
        [Fact]
        public void NotNullOrEmpty_EnumComparisonWorks()
        {
            var model = new Model1();

            var errors = new List<ValidationResult>();

            Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
            Assert.Empty(errors);

            model.Flag = AnEnum.Value2;

            Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
            Assert.NotEmpty(errors);

            Assert.NotNull(errors[0].ErrorMessage);
            Assert.Contains(nameof(Model1.DependentProperty), errors[0].MemberNames);
        }

        private class Model1
        {
            public AnEnum Flag { get; set; } = AnEnum.Value1;

            [NotNullOrEmptyIf(PropertyMatchesValue = nameof(Flag), Value = nameof(AnEnum.Value2))]
            public string? DependentProperty { get; set; }
        }

        private enum AnEnum
        {
            Value1,
            Value2,
        }
    }
}
