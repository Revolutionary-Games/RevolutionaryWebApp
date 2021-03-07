namespace ThriveDevCenter.Server
{
    using System;
    using System.Linq;
    using BlazorPagination;

    public static class PagedResultHelpers
    {
        public static PagedResult<TResult> ConvertResult<TSource, TResult>(this PagedResult<TSource> result,
            Func<TSource, TResult> conversion)
            where TSource : class
            where TResult : class
        {
            return new()
            {
                CurrentPage = result.CurrentPage,
                Results = result.Results.Select(conversion).ToArray(),
                PageCount = result.PageCount,
                PageSize = result.PageSize,
                RowCount = result.RowCount
            };
        }
    }
}
