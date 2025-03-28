namespace RevolutionaryWebApp.Shared.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;

public class PageRedirectDTO : IIdentifiable
{
    [Required]
    [StringLength(256, MinimumLength = 2)]
    public string FromPath { get; set; } = string.Empty;

    [Required]
    [StringLength(300, MinimumLength = 2)]
    public string ToUrl { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    [JsonIgnore]
    public long Id => FromPath.GetHashCode();
}
