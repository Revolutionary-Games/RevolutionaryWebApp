namespace ThriveDevCenter.Shared
{
    using System;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;

    public static class LinqHelpers
    {
        public static IQueryable<T> OrderBy<T>(this IQueryable<T> source, string column, SortDirection direction)
        {
            if (source == null)
                return null;

            // This logic is from Blazor.Pagination Licensed under the MIT license (this is a modified version)
            // https://github.com/villainoustourist/Blazor.Pagination/blob/24f0c938e5ecdab4eb605a41b77b9b27caa11947/src/Extensions.cs#L13
            var expression = source.Expression;
            var parameter = Expression.Parameter(typeof(T), "x");
            var selector = Expression.PropertyOrField(parameter, column);

            // Only allow sorting by explicitly allowed properties
            var attribute = selector.Member.GetCustomAttribute(typeof(AllowSortingByAttribute));

            if (attribute == null)
                throw new ArgumentException($"sorting by the selected property ({column}) is not allowed");

            var method = direction == SortDirection.Descending ? "OrderByDescending" : "OrderBy";
            expression = Expression.Call(typeof(Queryable), method,
                new[] { source.ElementType, selector.Type },
                expression, Expression.Quote(Expression.Lambda(selector, parameter)));
            return source.Provider.CreateQuery<T>(expression);
        }
    }
}
