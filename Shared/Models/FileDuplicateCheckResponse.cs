namespace RevolutionaryWebApp.Shared.Models;

using System;

/// <summary>
///   Response to a duplicate check when it is a duplicate. When not a duplicate the server responds with no content.
/// </summary>
public class FileDuplicateCheckResponse
{
    public long PreviousVersionSize { get; set; }
    public DateTime PreviousVersionTime { get; set; }
}
