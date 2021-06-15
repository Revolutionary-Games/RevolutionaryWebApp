namespace ThriveDevCenter.Shared.Converters
{
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

            result += $"{timeSpan.Seconds}s";

            return result;
        }
    }
}
