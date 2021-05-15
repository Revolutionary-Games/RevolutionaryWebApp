namespace ThriveDevCenter.Client.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Timers;
    using Microsoft.AspNetCore.Components;
    using ThriveDevCenter.Shared;
    using ThriveDevCenter.Shared.Models;
    using ThriveDevCenter.Shared.Notifications;

    /// <summary>
    ///   Base for data pages that don't use pagination (all data is fetched at once)
    /// </summary>
    public abstract class DataPage<T, TData> : ComponentBase, IAsyncDisposable
        where T : class, IIdentifiable
        where TData : class
    {
        [Parameter]
        public string DefaultSortColumn { get; set; }

        [Parameter]
        public SortDirection DefaultSortDirection { get; set; }

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

        public string Error { get; protected set; }

        /// <summary>
        ///   True when fetching new data. Can be used for example to show a loading spinner
        /// </summary>
        public bool FetchInProgress { get; protected set; }

        /// <summary>
        ///   True when fetching status should be shown to the user
        /// </summary>
        public bool VisibleFetchInProgress { get; protected set; }

        public bool AutoFetchDataOnInit { get; set; } = true;

        public abstract bool NoItemsFound { get; }

        protected int DataFetchCount { get; private set; }

        protected bool WantsToFetchDataAgain
        {
            get => wantsToFetchDataAgain;
            set
            {
                if (wantsToFetchDataAgain == value)
                    return;

                wantsToFetchDataAgain = value;
                fetchStartTimer.Interval = FetchTimerInterval;
                fetchStartTimer.Enabled = wantsToFetchDataAgain;
            }
        }

        /// <summary>
        ///   Computes the fetch timer interval based on how many times data has been fetched
        /// </summary>
        private int FetchTimerInterval
        {
            get
            {
                if (DataFetchCount < AppInfo.LongerTableRefreshIntervalCutoff)
                    return AppInfo.DefaultTableNotificationFetchTimer;

                if (DataFetchCount < AppInfo.LongestTableRefreshIntervalCutoff)
                    return AppInfo.LongerTableNotificationFetchTimer;

                return AppInfo.LongestTableNotificationFetchTimer;
            }
        }

        protected readonly SortHelper Sort;

        protected TData Data;

        private readonly Timer fetchStartTimer;
        private bool wantsToFetchDataAgain;

        protected DataPage(SortHelper sort)
        {
            Sort = sort;
            fetchStartTimer = new Timer { Interval = FetchTimerInterval };
            fetchStartTimer.Elapsed += OnFetchTimer;

            DefaultSortDirection = sort.Direction;

            if (string.IsNullOrEmpty(DefaultSortColumn))
                DefaultSortColumn = Sort.SortColumn;
        }

        public async Task ChangeSort(string column)
        {
            if (Sort.ColumnClick(column))
                await OnSortChanged();

            await FetchData();
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
                    await SingleItemUpdateReceived(notification.Item);

                    break;
                }
                case ListItemChangeType.ItemDeleted:
                case ListItemChangeType.ItemAdded:
                {
                    // For these the only 100% working solution is to basically fetch the current page again
                    // We could make a 99% working solution by comparing the current items on the client to determine
                    // if the data is part of this page or not, before refreshing
                    WantsToFetchDataAgain = true;

                    Console.WriteLine(
                        "Refreshing current paginated page due to item add or remove. Delay to avoid too " +
                        $"many requests: {FetchTimerInterval}");
                    break;
                }
            }
        }

        public virtual ValueTask DisposeAsync()
        {
            fetchStartTimer?.Dispose();
            return ValueTask.CompletedTask;
        }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            if (AutoFetchDataOnInit)
                await FetchData();
        }

        protected async Task FetchData(bool hidden = false)
        {
            if (FetchInProgress)
            {
                // Queue a separate fetch if already fetching
                Console.WriteLine("Already fetching paginated page, queued another fetch");
                WantsToFetchDataAgain = true;

                // Make fetch visible, if wanted. This doesn't fully solve things as the queued fetch will be in
                // the background, but at least the spinner will be visible for some time
                if (!hidden && !VisibleFetchInProgress)
                    VisibleFetchInProgress = true;

                return;
            }

            wantsToFetchDataAgain = false;
            FetchInProgress = true;
            if (!hidden)
                VisibleFetchInProgress = true;

            ++DataFetchCount;

            var requestParams = CreatePageRequestParams();

            var query = StartQuery(requestParams);

            PruneRequestParams(requestParams);

            await OnQuerySent(requestParams);

            await InvokeAsync(StateHasChanged);

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

            await OnDataReceived();
            await InvokeAsync(StateHasChanged);
        }

        protected virtual Dictionary<string, string> CreatePageRequestParams()
        {
            return new()
            {
                { "sortColumn", Sort.SortColumn },
                { "sortDirection", Sort.Direction.ToString() }
            };
        }

        /// <summary>
        ///   Removes the params that have the default values (currently in this object,
        ///   params aren't parsed back from queryParams)
        /// </summary>
        protected virtual void PruneRequestParams(Dictionary<string, string> queryParams)
        {
            if (Sort.SortColumn == DefaultSortColumn)
                queryParams.Remove("sortColumn");

            if (Sort.Direction == DefaultSortDirection)
                queryParams.Remove("sortDirection");
        }

        /// <summary>
        ///   Starts the actual query to fetch data from the server
        /// </summary>
        /// <param name="requestParams">The parameters to query with</param>
        protected abstract Task<TData> StartQuery(Dictionary<string, string> requestParams);

        /// <summary>
        ///   Child classes can update the current url or whatever they want here once a query is sent
        /// </summary>
        /// <param name="requestParams">The params the query was sent with, this is now safe to modify</param>
        protected virtual Task OnQuerySent(Dictionary<string, string> requestParams)
        {
            return Task.CompletedTask;
        }

        protected virtual Task OnDataReceived()
        {
            return Task.CompletedTask;
        }

        protected virtual Task OnSortChanged()
        {
            return Task.CompletedTask;
        }

        protected abstract Task SingleItemUpdateReceived(T updatedItem);

        private async void OnFetchTimer(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            if (FetchInProgress)
                return;

            // Can now start a new fetch

            // First disable this timer
            fetchStartTimer.Enabled = false;

            // Then start the fetch
            if (WantsToFetchDataAgain)
            {
                await FetchData(true);
            }
        }
    }

    public abstract class ListDataPage<T> : DataPage<T, List<T>>
        where T : class, IIdentifiable
    {
        public override bool NoItemsFound => Data is { Count: < 1 };

        protected ListDataPage(SortHelper sort) : base(sort) { }

        protected override async Task SingleItemUpdateReceived(T updatedItem)
        {
            for (int i = 0; i < Data.Count; ++i)
            {
                if (Data[i].Id == updatedItem.Id)
                {
                    // Found an item to replace
                    Data[i] = updatedItem;
                    await InvokeAsync(StateHasChanged);
                    break;
                }
            }
        }
    }
}
