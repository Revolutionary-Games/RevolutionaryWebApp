namespace ThriveDevCenter.Server.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using BlazorPagination;

    public static class ControllerQueryHelpers
    {
        public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
            this IAsyncEnumerable<T> enumerable,
            int page,
            int pageSize)
            where T : class
        {
            var allData = await enumerable.ToArrayAsync();

            // Logic duplicated mostly from BlazorPagination with modifications

            int num = allData.Length;
            page = page < 1 ? 1 : page;

            var result = new PagedResult<T>
            {
                CurrentPage = page, PageSize = pageSize, RowCount = num
            };

            if (num > 0)
            {
                result.PageCount = (int) Math.Ceiling((double) result.RowCount / (double) pageSize);

                page = Math.Min(result.PageCount, page);

                result.Results = allData.AsEnumerable().Skip((page - 1) * pageSize).Take(pageSize).ToArray();
            }
            else
            {
                result.Results = allData;
            }

            return result;
        }
        
        /*public static IAsyncEnumerable<T> ThenOrderBy<T>(this IAsyncEnumerable<T> source, string column, SortDirection direction,
            IEnumerable<string> extraAllowedColumns = null)
        {
            if (source == null)
                return null;

            // Modified logic from the IQueryable OrderBy

            var parameter = Expression.Parameter(typeof(T), "x");
            var selector = Expression.PropertyOrField(parameter, column);

            LinqHelpers.CheckTargetColumn<T>(column, extraAllowedColumns, selector);

            var method = direction == SortDirection.Descending ? "OrderByDescending" : "OrderBy";
            var expression = Expression.Call(typeof(Queryable), method,
                new[] { selector.Type },
                Expression.Quote(Expression.Lambda(selector, parameter)));

            // return source.CreateQuery<T>(expression);
        } */
    }
}
