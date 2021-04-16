namespace ThriveDevCenter.Client.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Components;
    using Services;
    using ThriveDevCenter.Shared.Models;
    using ThriveDevCenter.Shared.Notifications;

    /// <summary>
    ///   Base class for blazor pages that show a single resource
    /// </summary>
    public abstract class SingleResourcePage<T, TNotification> : ComponentBase, INotificationHandler<TNotification>
        where T : class, IIdentifiable
        where TNotification : ModelUpdated<T>
    {
        private bool dataReceived;

        /// <summary>
        ///   Id of the resource to show
        /// </summary>
        [Parameter]
        public long Id { get; set; }

        /// <summary>
        ///   Contains any errors encountered when fetching the data
        /// </summary>
        public string Error { get; protected set; }

        /// <summary>
        ///   True on the initial resource fetch
        /// </summary>
        public bool Loading { get; protected set; } = true;

        public T Data { get; private set; }

        public Task Handle(TNotification notification, CancellationToken cancellationToken)
        {
            // TODO: could buffer the update if we are currently fetching data
            if (Data == null)
                return Task.CompletedTask;

            if (Data.Id == notification.Item.Id)
            {
                // Received an update for our data
                Data = notification.Item;
                return InvokeAsync(StateHasChanged);
            }

            return Task.CompletedTask;
        }

        public abstract void GetWantedListenedGroups(UserAccessLevel currentAccessLevel, ISet<string> groups);

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            Loading = true;
            await FetchData();
        }

        protected async Task FetchData()
        {
            var query = StartQuery();

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

        /// <summary>
        ///   Starts the actual query to fetch data from the server
        /// </summary>
        protected abstract Task<T> StartQuery();

        /// <summary>
        ///   Useful for registering to receive notifications about data updates. This is done this way to avoid trying
        ///   to register for non-existent object's updates
        /// </summary>
        protected virtual Task OnFirstDataReceived()
        {
            return Task.CompletedTask;
        }
    }
}
