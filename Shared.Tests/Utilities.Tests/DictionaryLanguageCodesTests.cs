namespace RevolutionaryWebApp.Shared.Tests.Utilities.Tests;

using Shared.Utilities;
using Xunit;

public class DictionaryLanguageCodesTests
{
    [Theory]
    [InlineData("zh_CN", "zh-Hans")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh_Hans", "zh-Hans")]
    [InlineData("zh_TW", "zh-Hant")]
    [InlineData("zh-Hant", "zh-Hant")]
    [InlineData("pt_BR", "pt-BR")]
    [InlineData("sr_Latn_RS", "sr-Latn-RS")]
    [InlineData("fi", "fi")]
    public void NormalizesAndFindsEquivalentSpellings(string code, string preferred)
    {
        Assert.Equal(preferred, DictionaryLanguageCodes.Normalize(code));
        var equivalents = DictionaryLanguageCodes.GetEquivalentCodes(code);
        Assert.Equal(preferred, equivalents[0]);
        Assert.Contains(code, equivalents);
        foreach (var equivalent in equivalents)
        {
            Assert.Equal(preferred, DictionaryLanguageCodes.Normalize(equivalent));
            Assert.Equal(equivalents, DictionaryLanguageCodes.GetEquivalentCodes(equivalent));
        }
    }

    [Fact]
    public void KeepsDifferentLanguagesSeparate()
    {
        Assert.DoesNotContain("zh-Hant", DictionaryLanguageCodes.GetEquivalentCodes("zh_CN"));
        Assert.DoesNotContain("pt-PT", DictionaryLanguageCodes.GetEquivalentCodes("pt_BR"));
        Assert.Equal(new[] { "fi" }, DictionaryLanguageCodes.GetEquivalentCodes("fi"));
    }
}
