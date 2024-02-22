namespace RevolutionaryWebApp.Server.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using BlazorPagination;
using Shared;

public static class ControllerQueryHelpers
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IAsyncEnumerable<T> enumerable,
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
            CurrentPage = page, PageSize = pageSize, RowCount = num,
        };

        if (num > 0)
        {
            result.PageCount = (int)Math.Ceiling(result.RowCount / (double)pageSize);

            page = Math.Min(result.PageCount, page);

            result.Results = allData.AsEnumerable().Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        }
        else
        {
            result.Results = allData;
        }

        return result;
    }

    public static IOrderedAsyncEnumerable<T> OrderBy<T>(this IAsyncEnumerable<T> source, string column,
        SortDirection direction, IEnumerable<string>? extraAllowedColumns = null)
    {
        var lambda = CreatePropertySelector<T>(column, extraAllowedColumns);

        if (direction == SortDirection.Descending)
        {
            return source.OrderByDescending(lambda);
        }

        return source.OrderBy(lambda);
    }

    public static IOrderedAsyncEnumerable<T> ThenBy<T>(this IOrderedAsyncEnumerable<T> source, string column,
        SortDirection direction, IEnumerable<string>? extraAllowedColumns = null)
    {
        var lambda = CreatePropertySelector<T>(column, extraAllowedColumns);

        if (direction == SortDirection.Descending)
        {
            return source.ThenByDescending(lambda);
        }

        return source.ThenBy(lambda);
    }

    private static Func<T, object> CreatePropertySelector<T>(string column, IEnumerable<string>? extraAllowedColumns)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var selector = Expression.PropertyOrField(parameter, column);

        LinqHelpers.CheckTargetColumn(column, extraAllowedColumns, selector);

        var cast = Expression.Convert(selector, typeof(object));

        return (Func<T, object>)Expression.Lambda(cast, parameter).Compile();
    }
}
