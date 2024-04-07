namespace RevolutionaryWebApp.Server.Utilities;

using System;

[AttributeUsage(AttributeTargets.Property)]
public class UpdateFromClientRequestAttribute : Attribute
{
    /// <summary>
    ///   If not null, overrides the property where an update is looked for
    /// </summary>
    public string? RequestPropertyName { get; set; }

    public int MaxLengthWhenDisplayingChanges { get; set; } = 1500;
}

/// <summary>
///   If not null, overrides the property where an update is looked for
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ConvertWithWhenUpdatingFromClientAttribute : Attribute
{
    public ConvertWithWhenUpdatingFromClientAttribute(string converterMethodName)
    {
        ConverterMethodName = converterMethodName;
    }

    public string ConverterMethodName { get; set; }
}
