namespace ThriveDevCenter.Server.Tests.Utilities.Tests;

using Server.Utilities;
using Xunit;

public class HtmlStringExtensionsTests
{
    private const string TestString1 =
        @"<p><small>			<a href=""https://revolutionarygamesstudio.com/progress-update-04-30-2022-patch-0-5-8-1/"" class=""inline-onebox"">Progress Update 04/30/2022 | Patch 0.5.8.1 - Revolutionary Games Studio</a><br>
</small><br>Update 0.5.8.1 is now out, and introduces a good amount of performance improvements, among other things. We hope that this significantly improves the enjoyability of Thrive, and look forward to hearing any feedback on the changes. This week we focused on performance, and the causes behind it. We eventually discovered that the spawn system was…</p>
            <p><small>2 posts - 1 participant</small></p>
            <p><a href=""https://community.revolutionarygamesstudio.com/t/progress-update-04-30-2022-patch-0-5-8-1/4722"">Read full topic</a></p>";

    private const string TestResult1 =
        @"<p><small>			<a href=""https://revolutionarygamesstudio.com/progress-update-04-30-2022-patch-0-5-8-1/"" class=""inline-onebox"">Progress Update 04/30/2022 | Patch 0.5.8.1 - Revolutionary Games Studio</a><br>
</small><br>Update 0.5.8.1 is now out, and introduces a good amount of performance improvements, among other things. We hope that this significantly improves the enjoy...(continued)</p>";

    [Fact]
    public void HtmlExtension_TruncatesExample1()
    {
        Assert.Equal(TestResult1, TestString1.HtmlTruncate(300));
    }
}
