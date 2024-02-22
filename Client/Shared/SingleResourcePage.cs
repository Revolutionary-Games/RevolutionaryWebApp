namespace RevolutionaryWebApp.Client.Shared;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Microsoft.AspNetCore.Components;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Notifications;
using Services;

/// <summary>
///   Base class for blazor pages that show a single resource
/// </summary>
public abstract class SingleResourcePage<T, TNotification, TID> : SingleResourcePage<T, TID>,
    INotificationHandler<TNotification>
    where T : class, IIdentifiable, new()
    where TNotification : ModelUpdated<T>
    where TID : IEquatable<TID>
{
    public Task Handle(TNotification notification, CancellationToken cancellationToken)
    {
        // TODO: could buffer the update if we are currently fetching data
        if (Data == null)
            return Task.CompletedTask;

        if (Data.Id == notification.Item.Id)
        {
            // Received an update for our data
            if (OnUpdateNotificationReceived(notification.Item))
                Data = notification.Item;

            return InvokeAsync(StateHasChanged);
        }

        return Task.CompletedTask;
    }

    public abstract void GetWantedListenedGroups(IUserGroupData currentUserGroups, ISet<string> groups);

    /// <summary>
    ///   Called when an update notification is received for the shown resource
    /// </summary>
    /// <param name="newData">The new data</param>
    /// <returns>If this returns false the new item won't be used to override current data</returns>
    protected virtual bool OnUpdateNotificationReceived(T newData)
    {
        return true;
    }
}

/// <summary>
///   Base class for blazor pages that show a single resource without update notifications
/// </summary>
public abstract class SingleResourcePage<T, TID> : SimpleResourceFetcher<T>
    where T : class
    where TID : IEquatable<TID>
{
    private TID? previouslyFetchedId;

    /// <summary>
    ///   Id of the resource to show
    /// </summary>
    [Parameter]
    [EditorRequired]
    public TID Id { get; set; } = default!;

    protected override Task OnParametersSetAsync()
    {
        base.OnParametersSetAsync();

        if (!Id.Equals(previouslyFetchedId))
        {
            // Changed to showing a different entity
            dataReceived = false;
            return FetchData();
        }

        return Task.CompletedTask;
    }

    protected override async Task FetchData()
    {
        var query = StartQuery();
        previouslyFetchedId = Id;

        try
        {
            Data = await query;
        }
        catch (HttpRequestException e)
        {
            // Error write is not used here as we don't want to cause the blazor standard uncaught error popup
            Console.WriteLine($"Error getting single item data: {e}");

            if (e.StatusCode != HttpStatusCode.NotFound)
            {
                Error = $"Error fetching data: {e.Message}";
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error getting single item data: {e}");
            Error = $"Error fetching data: {e.Message}";
        }

        Loading = false;

        if (Data != null)
        {
            if (!dataReceived)
            {
                await OnFirstDataReceived();
                dataReceived = true;
            }
        }

        await InvokeAsync(StateHasChanged);
    }
}
