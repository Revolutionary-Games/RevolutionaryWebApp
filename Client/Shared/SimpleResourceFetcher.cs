namespace RevolutionaryWebApp.Client.Shared;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

public abstract class SimpleResourceFetcher<T> : ComponentBase
    where T : class
{
    protected bool dataReceived;

    /// <summary>
    ///   Contains any errors encountered when fetching the data
    /// </summary>
    public string? Error { get; protected set; }

    public bool Loading { get; protected set; } = true;

    public T? Data { get; protected set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        Loading = true;
        await FetchData();
    }

    protected virtual async Task FetchData()
    {
        var query = StartQuery();

        try
        {
            Data = await query;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error getting simple resource: {e}");
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

    /// <summary>
    ///   Starts the actual query to fetch data from the server
    /// </summary>
    protected abstract Task<T?> StartQuery();

    /// <summary>
    ///   Useful for registering to receive notifications about data updates. This is done this way to avoid trying
    ///   to register for non-existent object's updates
    /// </summary>
    protected virtual Task OnFirstDataReceived()
    {
        return Task.CompletedTask;
    }
}
