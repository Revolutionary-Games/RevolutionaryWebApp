namespace RevolutionaryWebApp.Server.Services;

using System.Text;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using SharedBase.Utilities;

internal class OpenGraphHtmlFilter : IPageHtmlFilter
{
    public static bool LookForPreviewImage(INode node, ref string? previewImage)
    {
        switch (node)
        {
            case IHtmlImageElement imgImageElement:
            {
                if (!string.IsNullOrEmpty(imgImageElement.Source))
                {
                    previewImage = imgImageElement.Source;
                    return true;
                }

                break;
            }
        }

        foreach (var childNode in node.ChildNodes)
        {
            if (LookForPreviewImage(childNode, ref previewImage))
                return true;
        }

        return false;
    }

    public bool WantsToProcess(string html, PageHtmlContext context)
    {
        return context.RenderOpenGraphMeta && !string.IsNullOrWhiteSpace(html);
    }

    public ValueTask<bool> Process(IHtmlElement body, PageHtmlContext context)
    {
        const int maxLength = 250;
        string? previewImage = null;
        var description = new StringBuilder();

        foreach (var node in body.ChildNodes)
        {
            if (previewImage == null)
                LookForPreviewImage(node, ref previewImage);

            if (!HandleContentForOpenGraph(node, maxLength, description))
                break;
        }

        context.OpenGraphDescription = description.ToString();
        context.PreviewImage = previewImage;
        return ValueTask.FromResult(false);
    }

    private static bool HandleContentForOpenGraph(INode node, int maxLength, StringBuilder stringBuilder)
    {
        // Reach end if text length is too much when reaching this node
        if (stringBuilder.Length >= maxLength)
            return false;

        switch (node)
        {
            // Elements that we don't want to extract text from
            // TODO: some of the following should probably end the snippet if there's more than half text already
            case IHtmlImageElement:
            case IHtmlScriptElement:
            case IComment:
            case IHtmlAudioElement:
            case IHtmlAreaElement:
            case IHtmlButtonElement:
            case IHtmlCanvasElement:
            case IHtmlEmbedElement:
            case IHtmlFormElement:
            case IHtmlHeadElement:
            case IHtmlFieldSetElement:
            case IHtmlInlineFrameElement:
            case IHtmlInputElement:
            case IHtmlMarqueeElement:
            case IHtmlMediaElement:
            case IHtmlMenuElement:
            case IHtmlMenuItemElement:
            case IHtmlMetaElement:
            case IHtmlModElement:
            case IHtmlObjectElement:
            case IHtmlOptionElement:
            case IHtmlOptionsGroupElement:
            case IHtmlOutputElement:
            case IHtmlParamElement:
            case IHtmlPictureElement:
            case IHtmlProgressElement:
            case IHtmlSelectElement:
            case IHtmlSlotElement:
            case IHtmlSourceElement:
            case IHtmlStyleElement:
            case IHtmlTemplateElement:
            case IHtmlTextAreaElement:
            case IHtmlTimeElement:
            case IHtmlTitleElement:
            case IHtmlTrackElement:
            case IHtmlHrElement:
            {
                return true;
            }

            // Elements that have text parts extracted
            case IText text:
            {
                if (string.IsNullOrWhiteSpace(text.Data))
                    break;

                int remainingLength = maxLength - stringBuilder.Length;

                // Add whitespace between text parts
                if (stringBuilder.Length > 0 && !char.IsWhiteSpace(stringBuilder[^1]))
                {
                    stringBuilder.Append(' ');
                    --remainingLength;
                }

                // Stop processing if there wasn't any text left to extract
                if (remainingLength <= 0)
                    break;

                stringBuilder.Append(text.Data.Trim().Truncate(remainingLength));

                break;
            }

            case IHtmlHeadingElement headingElement:
            {
                // End if there's already a good amount of text
                if (stringBuilder.Length > 0.3f * maxLength)
                    return false;

                if (string.IsNullOrWhiteSpace(headingElement.Title))
                    break;

                int remainingLength = maxLength - stringBuilder.Length;

                if (stringBuilder.Length > 0 && !char.IsWhiteSpace(stringBuilder[^1]))
                {
                    stringBuilder.Append(' ');
                    --remainingLength;
                }

                stringBuilder.Append(headingElement.Title.ToUpperInvariant().Trim().Truncate(remainingLength));

                break;
            }

            case IHtmlDivElement divElement:
            {
                // Skip YouTube videos
                if (divElement.ClassList.Contains("youtube-placeholder"))
                    return true;

                break;
            }
        }

        foreach (var childNode in node.ChildNodes)
        {
            if (!HandleContentForOpenGraph(childNode, maxLength, stringBuilder))
            {
                return false;
            }
        }

        return true;
    }
}
