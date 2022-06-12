namespace ThriveDevCenter.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Shared;
using Utilities;

public abstract class FeedBase : UpdateableModel
{
    protected FeedBase(string name)
    {
        Name = name;
    }

    /// <summary>
    ///   Name of the feed. Used as a file name in URL paths for retrieving this feed's content.
    ///   Need to make sure that different types of feeds don't have conflicting names. Also HTML suffix can cause
    ///   duplicates that prevent retrieving some feeds.
    /// </summary>
    [Required]
    [UpdateFromClientRequest]
    [AllowSortingBy]
    public string Name { get; set; }

    /// <summary>
    ///   Max items to show in the feed results
    /// </summary>
    [UpdateFromClientRequest]
    public int MaxItems { get; set; } = int.MaxValue;

    public string? LatestContent { get; set; }

    public DateTime? ContentUpdatedAt { get; set; }
}
