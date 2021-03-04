namespace ThriveDevCenter.Client.Shared
{
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;
    using BlazorPagination;
    using Microsoft.AspNetCore.Components;
    using ThriveDevCenter.Shared;

    /// <summary>
    ///   Base for paginated pages
    /// </summary>
    public abstract class PaginatedPage<T> : ComponentBase
        where T : class
    {
        [Parameter]
        public string DefaultSortColumn { get; set; }

        [Parameter]
        public SortDirection DefaultSortDirection { get; set; } = SortDirection.Ascending;

        [Parameter]
        public int DefaultPageSize { get; set; } = 25;

        [QueryStringParameter]
        public int PageSize { get; protected set; }

        [QueryStringParameter]
        public int Page { get; protected set; } = 1;

        [QueryStringParameter]
        public string SortColumn
        {
            get => Sort.SortColumn;
            protected set
            {
                Sort.SortColumn = value;
            }
        }

        [QueryStringParameter]
        public SortDirection SortDirection
        {
            get => Sort.Direction;
            protected set
            {
                Sort.Direction = value;
            }
        }

        /// <summary>
        ///   True when fetching new data. Can be used for example to show a loading spinner
        /// </summary>
        public bool FetchInProgress { get; protected set; }

        /// <summary>
        ///   True when fetching status should be shown to the user
        /// </summary>
        public bool VisibleFetchInProgress { get; protected set; }

        protected readonly SortHelper Sort;

        protected PagedResult<T> Data;

        public override Task SetParametersAsync(ParameterView parameters)
        {
            PageSize = DefaultPageSize;

            return base.SetParametersAsync(parameters);
        }

        public async Task ChangeSort(string column)
        {
            Sort.ColumnClick(column);
            await FetchData();
        }

        public Task ChangePage(int page)
        {
            Page = page;
            return FetchData();
        }

        public string SortClass(string currentColumn)
        {
            return Sort?.SortClass(currentColumn) ?? string.Empty;
        }

        protected PaginatedPage(SortHelper sort)
        {
            Sort = sort;

            if (string.IsNullOrEmpty(DefaultSortColumn))
                DefaultSortColumn = Sort.SortColumn;
        }

        protected override async Task OnInitializedAsync()
        {
            await FetchData();
        }

        protected async Task FetchData(bool hidden = false)
        {
            FetchInProgress = true;
            if (!hidden)
                VisibleFetchInProgress = true;

            var requestParams = CreatePageRequestParams();

            var query = StartQuery(requestParams);

            PruneRequestParams(requestParams);

            await OnQuerySent(requestParams);

            StateHasChanged();
            Data = await query;
            FetchInProgress = false;
            VisibleFetchInProgress = false;
            StateHasChanged();
        }

        protected Dictionary<string, string> CreatePageRequestParams()
        {
            return new Dictionary<string, string>
            {
                { "sortColumn", Sort.SortColumn },
                { "sortDirection", Sort.Direction.ToString() },
                { "page", Page.ToString(CultureInfo.InvariantCulture) },
                { "pageSize", PageSize.ToString(CultureInfo.InvariantCulture) },
            };
        }

        /// <summary>
        ///   Removes the params that have the default values (currently in this object,
        ///   params aren't parsed back from queryParams)
        /// </summary>
        protected void PruneRequestParams(Dictionary<string, string> queryParams)
        {
            if (Page == 1)
                queryParams.Remove("page");

            if (PageSize == DefaultPageSize)
                queryParams.Remove("pageSize");

            if (Sort.SortColumn == DefaultSortColumn)
                queryParams.Remove("sortColumn");

            if (Sort.Direction == DefaultSortDirection)
                queryParams.Remove("sortDirection");
        }

        /// <summary>
        ///   Starts the actual query to fetch data from the server
        /// </summary>
        /// <param name="requestParams"></param>
        protected abstract Task<PagedResult<T>> StartQuery(Dictionary<string, string> requestParams);

        /// <summary>
        ///   Child classes can update the current url or whatever they want here once a query is sent
        /// </summary>
        /// <param name="requestParams">The params the query was sent with, this is now safe to modify</param>
        protected abstract Task OnQuerySent(Dictionary<string, string> requestParams);
    }
}
