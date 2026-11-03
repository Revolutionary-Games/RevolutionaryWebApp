namespace RevolutionaryWebApp.Client.Tests.Shared.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using RevolutionaryWebApp.Client.Shared;
using RevolutionaryWebApp.Shared;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Notifications;
using Xunit;

public class ListDataPageTests : BunitContext
{
    [Fact]
    public async Task StringIdsUseSharedFetchingSortingAndNotificationHandling()
    {
        Services.AddSingleton<ListState>();
        var component = Render<StringKeyList>();
        var state = Services.GetRequiredService<ListState>();
        await component.InvokeAsync(async () =>
        {
            var page = state.Page;
            Assert.Single(state.Queries);
            Assert.Equal(nameof(APIKeyDTO.CreatedAt), state.Queries[0]["sortColumn"]);
            Assert.Equal("Descending", state.Queries[0]["sortDirection"]);

            await page.ChangeSort(nameof(APIKeyDTO.LastUsedAt));
            Assert.Equal(2, state.Queries.Count);
            Assert.Equal(nameof(APIKeyDTO.LastUsedAt), state.Queries[1]["sortColumn"]);
            await page.ChangeSort(nameof(APIKeyDTO.LastUsedAt));
            Assert.Equal(3, state.Queries.Count);
            Assert.Equal("Ascending", state.Queries[2]["sortDirection"]);

            var updated = new APIKeyDTO { Id = "first", TotalUses = 12 };
            await page.HandleItemNotification(new APIKeyListUpdated { Item = updated });
            Assert.Same(updated, page.Data![0]);
            Assert.Equal("second", page.Data[1].Id);
            await page.HandleItemNotification(new APIKeyListUpdated
            {
                Type = ListItemChangeType.ItemDeleted, Item = updated,
            });
            Assert.Equal("second", Assert.Single(page.Data).Id);
            await page.HandleItemNotification(new APIKeyListUpdated
            {
                Type = ListItemChangeType.ItemAdded, Item = new APIKeyDTO { Id = "third" },
            });
            Assert.Equal(new[] { "second", "third" }, page.Data.Select(item => item.Id));
        });
    }

    public class ListState
    {
        public StringKeyList Page { get; set; } = null!;
        public List<Dictionary<string, string?>> Queries { get; } = new();
    }

    public class StringKeyList : ListDataPage<APIKeyDTO, string>
    {
        public StringKeyList() : base(new SortHelper(nameof(APIKeyDTO.CreatedAt), SortDirection.Descending)) { }

        [Inject]
        public ListState State { get; set; } = null!;

        protected override async Task OnInitializedAsync()
        {
            State.Page = this;
            await base.OnInitializedAsync();
        }

        protected override string GetItemId(APIKeyDTO item)
        {
            return item.Id;
        }

        protected override Task<List<APIKeyDTO>?> StartQuery(Dictionary<string, string?> requestParams)
        {
            State.Queries.Add(new Dictionary<string, string?>(requestParams));
            return Task.FromResult<List<APIKeyDTO>?>(new List<APIKeyDTO>
            {
                new() { Id = "first" }, new() { Id = "second" },
            });
        }
    }
}
