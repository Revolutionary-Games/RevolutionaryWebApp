namespace ThriveDevCenter.Client.Shared
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Components;
    using ThriveDevCenter.Shared.Models;

    /// <summary>
    ///   Base class for blazor pages that show a single resource
    /// </summary>
    public abstract class SingleResourcePage<T> : ComponentBase
        where T : class, IIdentifiable
    {
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
            StateHasChanged();
        }

        /// <summary>
        ///   Starts the actual query to fetch data from the server
        /// </summary>
        protected abstract Task<T> StartQuery();
    }
}
