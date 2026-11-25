namespace RevolutionaryWebApp.Server.Services;

using System.Threading.Tasks;
using AngleSharp.Html.Dom;

internal interface IPageHtmlFilter
{
    /// <summary>
    ///   Used to detect if filtering is needed at all
    /// </summary>
    /// <returns>True if needs to filter</returns>
    public bool WantsToProcess(string html, PageHtmlContext context);

    /// <summary>
    ///   Processes the body by this filter
    /// </summary>
    /// <returns>Whether the filter changed the body markup.</returns>
    public ValueTask<bool> Process(IHtmlElement body, PageHtmlContext context);
}
