namespace ThriveDevCenter.Server.Utilities;

using System;
using System.Linq.Expressions;

/// <summary>
///   Helps in avoiding constants being "leaked" in query strings. This approach is from:
///   http://graemehill.ca/entity-framework-dynamic-queries-and-parameterization/
/// </summary>
public static class ExpressionHelper
{
    public static MemberExpression WrappedConstant<TValue>(TValue value)
    {
        var wrapper = new WrappedObj<TValue>(value);

        return Expression.Property(
            Expression.Constant(wrapper),
            wrapper.GetType().GetProperty("Value") ?? throw new InvalidOperationException());
    }

    private class WrappedObj<TValue>
    {
        public WrappedObj(TValue value)
        {
            Value = value;
        }

        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public TValue Value { get; }
    }
}
