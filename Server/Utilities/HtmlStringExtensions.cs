namespace ThriveDevCenter.Server.Utilities;

using System;
using System.IO;
using System.Linq;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Shared.Converters;

public static class HtmlStringExtensions
{
    private const string AfterEllipsisText = "(continued)";

    /// <summary>
    ///   Implements a special truncate that respects HTML formatting
    /// </summary>
    /// <param name="str">
    ///   The string containing HTML to truncate (if parsing fails, normal truncate is performed)
    /// </param>
    /// <param name="length">
    ///   The length after which to truncate (the ellipsis and truncate message is not included in this length)
    /// </param>
    /// <returns>The truncated string</returns>
    public static string HtmlTruncate(this string str, int length = 300)
    {
        if (str.Length <= length)
        {
            return str;
        }

        var parser = new HtmlParser(new HtmlParserOptions()
        {
            IsStrictMode = false,
        });

        var dom = CreateDummyDom();

        INodeList document;
        try
        {
            document = parser.ParseFragment(str, dom);
        }
        catch (Exception)
        {
            return str.Truncate(length);
        }

        int seenLength = 0;

        foreach (var topLevelNode in document)
        {
            foreach (var node in topLevelNode.GetDescendants())
            {
                if (seenLength > length)
                {
                    node.RemoveFromParent();
                    continue;
                }

                if (node is IText text)
                {
                    if (seenLength + text.Data.Length > length)
                    {
                        text.Data = text.Data.Truncate(length - seenLength) + AfterEllipsisText;
                        seenLength += text.Data.Length;
                    }
                }
                else
                {
                    // Let's add some length from non text nodes
                    seenLength += node.NodeName.Length * 2 + 4;

                    // And their attributes
                    if (node is IElement element)
                    {
                        seenLength += element.Attributes.Sum(a => a.Name.Length + a.Value.Length);
                    }
                }
            }
        }

        using var stream = new MemoryStream();
        stream.Capacity = length;

        using var writer = new StreamWriter(stream, Encoding.UTF8);

        var formatter = new HtmlMarkupFormatter();

        // The second part here is not needed for the kind of data we handle so, we can skip that
        foreach (var node in document.Where(n => n.HasChildNodes))
        {
            if (node is IHtmlBodyElement)
            {
                // We don't want this to be written in the output, so only output the children
                foreach (var childNode in node.ChildNodes)
                {
                    childNode.ToHtml(writer, formatter);
                }
            }
            else
            {
                node.ToHtml(writer, formatter);
            }
        }

        writer.Flush();

        stream.Position = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var result = reader.ReadToEnd();
        return result;
    }

    private static IElement CreateDummyDom()
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument(string.Empty);
        return document.DocumentElement;
    }
}
