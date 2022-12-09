namespace ThriveDevCenter.Shared.Converters;

using System;

public static class TimeSpanExtensions
{
    public static string ToShortForm(this TimeSpan timeSpan)
    {
        string result = string.Empty;

        if (timeSpan.Days > 0)
        {
            result += $"{timeSpan.Days}d";
        }

        if (timeSpan.Hours > 0)
        {
            result += $"{timeSpan.Hours}h";
        }

        if (timeSpan.Minutes > 0)
        {
            result += $"{timeSpan.Minutes}m";
        }

        if (string.IsNullOrEmpty(result) && timeSpan.TotalSeconds < -60)
        {
            // We have the time range the wrong way around or future times are coming from the database
            return timeSpan.ToString();
        }

        result += $"{timeSpan.Seconds}s";

        return result;
    }
}
