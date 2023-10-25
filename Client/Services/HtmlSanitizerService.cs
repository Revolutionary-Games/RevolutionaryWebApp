namespace ThriveDevCenter.Client.Services;

using System;
using Ganss.Xss;

public class HtmlSanitizerService
{
    private readonly HtmlSanitizer sanitizer = new();

    public HtmlSanitizerService()
    {
        sanitizer.AllowedAttributes.Add("class");
        sanitizer.AllowedClasses.Add("math");
    }

    public string SanitizeHtml(string html)
    {
        // Uncomment for debugging why output is not working well
        // return DebugSanitize(html);

        var result = sanitizer.Sanitize(html);
        return result;
    }

    private string DebugSanitize(string html)
    {
        Console.WriteLine("Sanitizing: " + html);
        var result = sanitizer.Sanitize(html);
        Console.WriteLine("Result: " + result);
        return result;
    }
}
