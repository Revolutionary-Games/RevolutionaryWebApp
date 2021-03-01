namespace ThriveDevCenter.Client.Shared
{
    using System.Threading.Tasks;
    using BlazorPagination;
    using Microsoft.AspNetCore.Components;
    using ThriveDevCenter.Shared;

    /// <summary>
    ///   Base for paginated pages
    /// </summary>
    public abstract class PaginatedPage<T> : ComponentBase
        where T : class
    {
        [Parameter]
        public string DefaultSortColumn { get; set; }

        [Parameter]
        public SortDirection DefaultSortDirection { get; set; } = SortDirection.Ascending;

        [Parameter]
        public int DefaultPageSize { get; set; } = 5;

        protected readonly SortHelper Sort;

        protected PagedResult<T> Data;
        protected int Page = 1;
        protected int PageSize = 5;

        public async Task ChangeSort(string column)
        {
            Sort.ColumnClick(column);
            await FetchData();
        }

        protected PaginatedPage(SortHelper sort)
        {
            Sort = sort;

            if (string.IsNullOrEmpty(DefaultSortColumn))
                DefaultSortColumn = Sort.SortColumn;
        }

        protected override async Task OnInitializedAsync()
        {
            await FetchData();
        }

        protected abstract Task FetchData();
    }
}
