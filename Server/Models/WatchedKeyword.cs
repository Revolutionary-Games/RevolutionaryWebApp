namespace ThriveDevCenter.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Shared;

public class WatchedKeyword
{
    public WatchedKeyword(string keyword)
    {
        Keyword = keyword;
    }

    [Key]
    [AllowSortingBy]
    public string Keyword { get; set; } = null!;

    public DateTimeOffset? LastSeen { get; set; } = DateTimeOffset.Now;

    public int TotalCount { get; set; } = 0;
}
