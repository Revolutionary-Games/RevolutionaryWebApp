namespace ThriveDevCenter.Client.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;
    using BlazorPagination;
    using Microsoft.AspNetCore.Components;
    using ThriveDevCenter.Shared;
    using ThriveDevCenter.Shared.Models;
    using ThriveDevCenter.Shared.Notifications;

    /// <summary>
    ///   Base for paginated pages
    /// </summary>
    public abstract class PaginatedPage<T> : ComponentBase
        where T : class, IIdentifiable
    {
        [Parameter]
        public string DefaultSortColumn { get; set; }

        [Parameter]
        public SortDirection DefaultSortDirection { get; set; }

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

        public string Error { get; private set; }

        /// <summary>
        ///   True when fetching new data. Can be used for example to show a loading spinner
        /// </summary>
        public bool FetchInProgress { get; protected set; }

        /// <summary>
        ///   True when fetching status should be shown to the user
        /// </summary>
        public bool VisibleFetchInProgress { get; protected set; }

        public bool NoItemsFound => Data != null && Data.Results.Length < 1;

        protected readonly SortHelper Sort;

        protected PagedResult<T> Data;

        public override Task SetParametersAsync(ParameterView parameters)
        {
            PageSize = DefaultPageSize;

            return base.SetParametersAsync(parameters);
        }

        public async Task ChangeSort(string column)
        {
            if (Sort.ColumnClick(column))
            {
                // Move to first page when sort column is changed
                Page = 1;
            }

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

        public async Task HandleItemNotification(ListUpdated<T> notification)
        {
            switch (notification.Type)
            {
                case ListItemChangeType.ItemUpdated:
                {
                    if (notification.Item == null)
                    {
                        Console.WriteLine("Got a list notification update with empty Item, can't process this");
                        break;
                    }

                    // Can't apply if data not loaded
                    // TODO: could queue this and retry applying this in 1 second
                    if (Data == null)
                    {
                        Console.WriteLine("Got a list notification update before data was loaded, ignoring for now");
                        break;
                    }

                    // Replace the existing item if there is one loaded
                    for (int i = 0; i < Data.Results.Length; ++i)
                    {
                        if (Data.Results[i].Id == notification.Item.Id)
                        {
                            // Found an item to replace
                            Data.Results[i] = notification.Item;
                            await InvokeAsync(StateHasChanged);
                            break;
                        }
                    }

                    break;
                }
                case ListItemChangeType.ItemDeleted:
                case ListItemChangeType.ItemAdded:
                {
                    // TODO: add a timer here that prevents this from firing too often (after the first few times
                    // this should fire with a 15 second delay)

                    Console.WriteLine("Refreshing current paginated page due to item add or remove");

                    // For these the only 100% working solution is to basically fetch the current page again
                    // (but don't show the spinner to not annoy the user)
                    // TODO: should this only fetch if FetchInProgress is not true?
                    await FetchData(true);
                    break;
                }
            }
        }

        protected PaginatedPage(SortHelper sort)
        {
            Sort = sort;

            DefaultSortDirection = sort.Direction;

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

            try
            {
                Data = await query;
            }
            catch (Exception e)
            {
                // Error write is not used here as we don't want to cause the blazor standard uncaught error popup
                Console.WriteLine($"Error getting query results for a PaginatedPage: {e}");
                Error = $"Error fetching data: {e.Message}";
            }

            FetchInProgress = false;
            VisibleFetchInProgress = false;
            StateHasChanged();
        }

        protected virtual Dictionary<string, string> CreatePageRequestParams()
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
        /// <param name="requestParams">The parameters to query with</param>
        protected abstract Task<PagedResult<T>> StartQuery(Dictionary<string, string> requestParams);

        /// <summary>
        ///   Child classes can update the current url or whatever they want here once a query is sent
        /// </summary>
        /// <param name="requestParams">The params the query was sent with, this is now safe to modify</param>
        protected abstract Task OnQuerySent(Dictionary<string, string> requestParams);
    }
}
