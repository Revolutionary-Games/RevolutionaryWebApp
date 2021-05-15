namespace ThriveDevCenter.Client.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;
    using System.Timers;
    using BlazorPagination;
    using Microsoft.AspNetCore.Components;
    using ThriveDevCenter.Shared;
    using ThriveDevCenter.Shared.Models;
    using ThriveDevCenter.Shared.Notifications;

    /// <summary>
    ///   Base for paginated pages
    /// </summary>
    public abstract class PaginatedPage<T> : DataPage<T, PagedResult<T>>
        where T : class, IIdentifiable
    {
        [Parameter]
        public int DefaultPageSize { get; set; } = 25;

        [QueryStringParameter]
        public int PageSize { get; protected set; }

        [QueryStringParameter]
        public int Page { get; protected set; } = 1;

        public override bool NoItemsFound => Data != null && Data.Results.Length < 1;

        protected PaginatedPage(SortHelper sort) : base(sort)
        {
        }

        public override Task SetParametersAsync(ParameterView parameters)
        {
            PageSize = DefaultPageSize;

            return base.SetParametersAsync(parameters);
        }

        public Task ChangePage(int page)
        {
            Page = page;
            return FetchData();
        }

        protected override Dictionary<string, string> CreatePageRequestParams()
        {
            var result = base.CreatePageRequestParams();

            result["page"] = Page.ToString(CultureInfo.InvariantCulture);
            result["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture);
            return result;
        }

        protected override void PruneRequestParams(Dictionary<string, string> queryParams)
        {
            base.PruneRequestParams(queryParams);

            if (Page == 1)
                queryParams.Remove("page");

            if (PageSize == DefaultPageSize)
                queryParams.Remove("pageSize");
        }

        protected override Task OnSortChanged()
        {
            // Move to first page when sort column is changed
            Page = 1;
            return Task.CompletedTask;
        }

        protected override async Task SingleItemUpdateReceived(T updatedItem)
        {
            for (int i = 0; i < Data.Results.Length; ++i)
            {
                if (Data.Results[i].Id == updatedItem.Id)
                {
                    // Found an item to replace
                    Data.Results[i] = updatedItem;
                    await InvokeAsync(StateHasChanged);
                    break;
                }
            }
        }
    }
}
