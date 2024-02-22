namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;

public class WatchedKeyword
{
    public WatchedKeyword(string keyword, string title)
    {
        Keyword = keyword;
        Title = title;
    }

    [Key]
    public string Keyword { get; set; }

    public string Title { get; set; }

    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public int TotalCount { get; set; }
}
