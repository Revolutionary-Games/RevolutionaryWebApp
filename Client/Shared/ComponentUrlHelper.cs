namespace ThriveDevCenter.Client.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Components;
    using Microsoft.AspNetCore.WebUtilities;
    using Microsoft.JSInterop;

    public class ComponentUrlHelper
    {
        private readonly IJSRuntime jsRuntime;
        private readonly NavigationManager navigationManager;

        public ComponentUrlHelper(IJSRuntime jsRuntime, NavigationManager navigationManager)
        {
            this.jsRuntime = jsRuntime;
            this.navigationManager = navigationManager;
        }

        public async Task UpdateUrlHistoryIfChanged(Dictionary<string, string> newQueryParams)
        {
            var targetUri = QueryHelpers.AddQueryString(navigationManager.Uri.Split("?")[0], newQueryParams);

            // TODO: remove
            Console.WriteLine($"current uri : {navigationManager.Uri}");

            // Add new history entry if the uri would change
            if (targetUri != navigationManager.Uri)
                await jsRuntime.InvokeVoidAsync("addToHistory", targetUri);
        }
    }
}
