namespace ThriveDevCenter.Server.Formatters;

using System;
using System.Text;
using System.Threading.Tasks;
using AngleSharp.Io;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

public class HtmlTextFormatter : TextOutputFormatter
{
    public HtmlTextFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse(MimeTypeNames.Html));

        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
    }

    public override Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        if (context.Object == null)
            return Task.CompletedTask;

        var httpContext = context.HttpContext;
        return httpContext.Response.WriteAsync((string)context.Object, selectedEncoding);
    }

    protected override bool CanWriteType(Type? type) => typeof(string).IsAssignableFrom(type);
}
