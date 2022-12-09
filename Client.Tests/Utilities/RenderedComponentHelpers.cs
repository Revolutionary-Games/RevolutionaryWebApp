namespace ThriveDevCenter.Client.Tests.Utilities;

using System;
using Bunit;
using Microsoft.AspNetCore.Components;

public static class RenderedComponentHelpers
{
    /// <summary>
    ///   Flips when Find throws
    /// </summary>
    /// <param name="rendered">Rendered tree to use</param>
    /// <param name="cssSelector">What element to look for</param>
    /// <typeparam name="T">Rendered component type</typeparam>
    /// <exception cref="InvalidOperationException">Thrown when the element is found</exception>
    public static void FindIsNull<T>(this IRenderedComponent<T> rendered, string cssSelector)
        where T : IComponent
    {
        try
        {
            rendered.Find(cssSelector);
            throw new InvalidOperationException("element exists");
        }
        catch (ElementNotFoundException)
        {
        }
    }
}
