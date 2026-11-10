namespace RevolutionaryWebApp.Shared.Utilities;

using System;
using System.Text.RegularExpressions;

public static class Usernames
{
    public static readonly Regex MentionPattern = new(@"(?<!\w)@([A-Za-z0-9_-]{2,50})(?![A-Za-z0-9_-])",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));

    // The max length comes from CommunicationConstants.MAX_USERNAME_LENGTH
    private static readonly Regex ValidName = new(@"\A[A-Za-z0-9_-]{2,50}\z",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));

    public static bool IsValid(string? name)
    {
        return name != null && ValidName.IsMatch(name);
    }
}
