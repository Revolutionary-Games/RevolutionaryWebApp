namespace ThriveDevCenter.Shared;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

public static class LinqHelpers
{
    public static IQueryable<T> OrderBy<T>(this IQueryable<T> source, string column, SortDirection direction,
        IEnumerable<string>? extraAllowedColumns = null)
    {
        // This logic is from Blazor.Pagination Licensed under the MIT license (this is a modified version)
        // LineLengthCheckDisable
        // https://github.com/villainoustourist/Blazor.Pagination/blob/24f0c938e5ecdab4eb605a41b77b9b27caa11947/src/Extensions.cs#L13
        // LineLengthCheckEnable
        var expression = source.Expression;
        var parameter = Expression.Parameter(typeof(T), "x");
        var selector = Expression.PropertyOrField(parameter, column);

        CheckTargetColumn(column, extraAllowedColumns, selector);

        var method = direction == SortDirection.Descending ? "OrderByDescending" : "OrderBy";
        expression = Expression.Call(typeof(Queryable), method,
            new[] { source.ElementType, selector.Type },
            expression, Expression.Quote(Expression.Lambda(selector, parameter)));
        return source.Provider.CreateQuery<T>(expression);
    }

    public static void CheckTargetColumn(string column, IEnumerable<string>? extraAllowedColumns,
        MemberExpression selector)
    {
        var attribute = selector.Member.GetCustomAttribute(typeof(AllowSortingByAttribute));

        if (attribute == null &&
            (extraAllowedColumns == null || !extraAllowedColumns.Contains(selector.Member.Name)))
        {
            // Sorting by the "Key" field is explicitly allowed. This is needed for the User model that inherits
            // the id key, so we can't set attributes on that
            if (selector.Member.GetCustomAttribute(typeof(KeyAttribute)) == null)
                throw new ArgumentException($"sorting by the selected property ({column}) is not allowed");
        }
    }
}
