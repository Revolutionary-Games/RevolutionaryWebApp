namespace RevolutionaryWebApp.Shared;

using System;

/// <summary>
///   When placed on an attribute sort by string column name is allowed. <see cref="LinqHelpers.OrderBy{T}"/>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class AllowSortingByAttribute : Attribute
{
}
