namespace ThriveDevCenter.Client.Shared;

using System;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

public static class QueryStringParameterHelper
{
    /// <summary>
    ///   Used to set component parameters from a query string on the current url
    /// </summary>
    /// <param name="component">The target component</param>
    /// <param name="navigationManager">Where to get the URL from</param>
    /// <typeparam name="T">Type of component</typeparam>
    /// <exception cref="InvalidOperationException">If URI parse fails</exception>
    public static void SetParametersFromQueryString<T>(this T component,
        NavigationManager navigationManager)
        where T : ComponentBase
    {
        if (!Uri.TryCreate(navigationManager.Uri, UriKind.RelativeOrAbsolute, out var uri))
            throw new InvalidOperationException("Current URI is invalid, url: " + navigationManager.Uri);

        var queryParams = QueryHelpers.ParseQuery(uri.Query);

        // Apply the query parameter values to the properties
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                     BindingFlags.Instance | BindingFlags.FlattenHierarchy))
        {
            var data =
                property.GetCustomAttribute(typeof(QueryStringParameterAttribute), true) as
                    QueryStringParameterAttribute;

            if (data == null)
                continue;

            var name = data.Name ?? property.Name;

            foreach (var tuple in queryParams)
            {
                if (tuple.Key.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                {
                    // Found matching value
                    object convertedValue;

                    if (property.PropertyType.IsEnum)
                    {
                        convertedValue = Enum.Parse(property.PropertyType,
                            tuple.Value[0] ?? throw new Exception("Query parameter has null value"));
                    }
                    else
                    {
                        convertedValue = Convert.ChangeType(
                            tuple.Value[0] ?? throw new Exception("Query parameter has null value"),
                            property.PropertyType,
                            CultureInfo.InvariantCulture);
                    }

                    property.SetValue(component, convertedValue);
                    break;
                }
            }
        }
    }
}

/// <summary>
///   Marks that attribute can be read from a query string
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class QueryStringParameterAttribute : Attribute
{
    public QueryStringParameterAttribute()
    {
    }

    public QueryStringParameterAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    ///   Override name for the query string parameter
    /// </summary>
    public string? Name { get; }
}
