namespace RevolutionaryWebApp.Client.Shared;

using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Microsoft.AspNetCore.Components;
using RevolutionaryWebApp.Shared;
using RevolutionaryWebApp.Shared.Utilities;

public abstract class BaseFileBrowser<T> : PaginatedPage<T>
    where T : class, IIdentifiable, new()
{
    protected BaseFileBrowser() : base(new SortHelper("Name", SortDirection.Ascending))
    {
        DefaultPageSize = 100;
    }

    [Parameter]
    public string? FileBrowserPath { get; set; }

    [Parameter]
    [EditorRequired]
    public string BasePath { get; set; } = null!;

    [Parameter]
    [EditorRequired]
    public string RootFolderName { get; set; } = null!;

    /// <summary>
    ///   When true reacts to parameters changing by querying the server again
    /// </summary>
    public bool ReactToParameterChange { get; protected set; }

    public bool AutoSetReactToParameterChangeAfterDataReceived { get; protected set; } = true;

    protected string NonNullPath => FileBrowserPath ?? string.Empty;

    protected string CurrentPathSlashPrefix => "/" + NonNullPath;

    protected string SlashIfPathNotEmpty => string.IsNullOrEmpty(FileBrowserPath) ? string.Empty : "/";

    /// <summary>
    ///   Creates a link to navigate to sub folder
    /// </summary>
    /// <param name="name">Name of the sub folder</param>
    /// <param name="skipLastPart">
    ///   If true the last component of FileBrowserPath is ignored. Used when the last part can be a file
    /// </param>
    /// <returns>A navigation link URL</returns>
    protected string FolderLink(string name, bool skipLastPart = false)
    {
        var browserPath = NonNullPath;

        if (skipLastPart && browserPath.Contains('/'))
        {
            browserPath = PathParser.GetParentPath(browserPath);
        }

        var slash = string.IsNullOrEmpty(browserPath) ? string.Empty : "/";

        return $"{BasePath}{browserPath}{slash}{name}";
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        if (ReactToParameterChange)
            await FetchData();
    }

    protected override Task OnDataReceived()
    {
        if (AutoSetReactToParameterChangeAfterDataReceived)
            ReactToParameterChange = true;
        return base.OnDataReceived();
    }
}
