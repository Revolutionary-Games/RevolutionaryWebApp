namespace ThriveDevCenter.Client.Shared;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Timers;
using DevCenterCommunication.Models;
using Microsoft.AspNetCore.Components;
using ThriveDevCenter.Shared;
using ThriveDevCenter.Shared.Notifications;

/// <summary>
///   Base for data pages that don't use pagination (all data is fetched at once)
/// </summary>
public abstract class DataPage<T, TData> : ComponentBase, IAsyncDisposable
    where T : class, IIdentifiable, new()
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

    public string? Error { get; protected set; }

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
    protected int FetchTimerInterval
    {
        get
        {
            if (DataFetchCount < AppInfo.ShortTableRefreshIntervalCutoff)
                return AppInfo.ShortTableNotificationFetchTimer;

            if (DataFetchCount < AppInfo.LongerTableRefreshIntervalCutoff)
                return AppInfo.NormalTableNotificationFetchTimer;

            if (DataFetchCount < AppInfo.LongestTableRefreshIntervalCutoff)
                return AppInfo.LongerTableNotificationFetchTimer;

            return AppInfo.LongestTableNotificationFetchTimer;
        }
    }

    protected readonly SortHelper Sort;

    protected TData? Data;

    private readonly Timer fetchStartTimer;
    private readonly Timer fetchCountDecrementTimer;
    private bool wantsToFetchDataAgain;

    protected DataPage(SortHelper sort)
    {
        Sort = sort;
        fetchStartTimer = new Timer { Interval = FetchTimerInterval };
        fetchStartTimer.Elapsed += OnFetchTimer;

        fetchCountDecrementTimer = new Timer { Interval = AppInfo.ForgetDataRefreshFetchInterval };
        fetchCountDecrementTimer.Elapsed += OnDecrementFetchTimer;

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
        return Sort.SortClass(currentColumn);
    }

    /// <summary>
    ///   Handles an item update notification. Unlike the paginated page this class always has all the data loaded
    ///   (if data is loaded) so this can more comprehensively handle the notifications
    /// </summary>
    /// <param name="notification">The update notification received from the server</param>
    public virtual async Task HandleItemNotification(ListUpdated<T> notification)
    {
        switch (notification.Type)
        {
            case ListItemChangeType.ItemUpdated:
            {
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
            {
                if (Data == null)
                {
                    Console.WriteLine("Got a list notification delete before data was loaded, ignoring for now");
                    break;
                }

                await SingleItemDeleteReceived(notification.Item);
                break;
            }
            case ListItemChangeType.ItemAdded:
            {
                if (Data == null)
                {
                    Console.WriteLine("Got a list notification delete before data was loaded, ignoring for now");
                    break;
                }

                await SingleItemAddReceived(notification.Item);
                break;
            }
        }
    }

    public virtual ValueTask DisposeAsync()
    {
        fetchStartTimer.Dispose();
        fetchCountDecrementTimer.Dispose();
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

        if (!fetchCountDecrementTimer.Enabled)
            fetchCountDecrementTimer.Start();

        var requestParams = CreatePageRequestParams();

        var query = StartQuery(requestParams);

        PruneRequestParams(requestParams);

        await OnQuerySent(requestParams);

        await InvokeAsync(StateHasChanged);

        try
        {
            Data = await query;

            // Clear error on successful data retrieve
            Error = null;
        }
        catch (Exception e)
        {
            // Error write is not used here as we don't want to cause the blazor standard uncaught error popup
            Console.WriteLine($"Error getting query results for a PaginatedPage: {e}");
            Error = $"Error fetching data: {e.Message}";
        }

        if (Data == null)
            Console.WriteLine("Got null data query response in DataPage");

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
            { "sortDirection", Sort.Direction.ToString() },
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
    protected abstract Task<TData?> StartQuery(Dictionary<string, string> requestParams);

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

    protected virtual Task SingleItemDeleteReceived(T deletedItem)
    {
        return Task.CompletedTask;
    }

    protected virtual Task SingleItemAddReceived(T addedItem)
    {
        return Task.CompletedTask;
    }

    private async void OnFetchTimer(object? sender, ElapsedEventArgs elapsedEventArgs)
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

    private void OnDecrementFetchTimer(object? sender, ElapsedEventArgs elapsedEventArgs)
    {
        DataFetchCount = Math.Max(DataFetchCount - 1, 0);
    }
}

public abstract class ListDataPage<T> : DataPage<T, List<T>>
    where T : class, IIdentifiable, new()
{
    public override bool NoItemsFound => Data is { Count: < 1 };

    protected ListDataPage(SortHelper sort) : base(sort) { }

    protected override async Task SingleItemUpdateReceived(T updatedItem)
    {
        if (Data == null)
            return;

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

    protected override Task SingleItemDeleteReceived(T deletedItem)
    {
        if (Data == null)
            return Task.CompletedTask;

        bool deleted = Data.RemoveAll(i => i.Id == deletedItem.Id) > 0;

        if (deleted)
            return InvokeAsync(StateHasChanged);

        return Task.CompletedTask;
    }

    protected override Task SingleItemAddReceived(T addedItem)
    {
        if (Data == null)
            return Task.CompletedTask;

        // TODO: apply sort on item add to place it in the right place
        Data.Add(addedItem);
        return InvokeAsync(StateHasChanged);
    }
}
