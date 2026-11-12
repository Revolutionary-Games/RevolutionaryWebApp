namespace RevolutionaryWebApp.Shared.Models.Pages;

using System.ComponentModel.DataAnnotations;

public class SteamConversionRequest
{
    [Required(AllowEmptyStrings = true)]
    [StringLength(AppInfo.MaxPageLength)]
    public string Content { get; set; } = string.Empty;
}
