namespace ThriveDevCenter.Client.Shared
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Components;
    using ThriveDevCenter.Shared;
    using ThriveDevCenter.Shared.Models;

    public abstract class BaseFileBrowser<T> : PaginatedPage<T>
        where T : class, IIdentifiable
    {
        [Parameter]
        public string FileBrowserPath { get; set; }

        [Parameter]
        public string BasePath { get; set; }

        [Parameter]
        public string RootFolderName { get; set; }

        protected BaseFileBrowser() : base(new SortHelper("Name", SortDirection.Ascending))
        {
            DefaultPageSize = 100;
        }

        /// <summary>
        ///   When true reacts to parameters changing by querying the server again
        /// </summary>
        public bool ReactToParameterChange { get; protected set; }

        protected string NonNullPath => FileBrowserPath ?? string.Empty;

        protected string CurrentPathSlashPrefix => "/" + NonNullPath;

        protected string SlashIfPathNotEmpty => string.IsNullOrEmpty(FileBrowserPath) ? string.Empty : "/";

        protected string FolderLink(string name)
        {
            return $"{BasePath}{FileBrowserPath}{SlashIfPathNotEmpty}{name}";
        }

        protected override async Task OnParametersSetAsync()
        {
            await base.OnParametersSetAsync();

            if (ReactToParameterChange)
                await FetchData();
        }

        protected override Task OnDataReceived()
        {
            ReactToParameterChange = true;
            return base.OnDataReceived();
        }
    }
}
