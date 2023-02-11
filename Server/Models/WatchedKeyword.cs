namespace ThriveDevCenter.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;

public class WatchedKeyword
{
    public WatchedKeyword(string keyword)
    {
        Keyword = keyword;
    }

    [Key]
    public string Keyword { get; set; }

    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public int TotalCount { get; set; } = 0;
}
