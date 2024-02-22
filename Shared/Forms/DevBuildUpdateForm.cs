namespace RevolutionaryWebApp.Shared.Forms;

using System.ComponentModel.DataAnnotations;

public class DevBuildUpdateForm
{
    [StringLength(AppInfo.MaxDevBuildDescriptionLength, MinimumLength = AppInfo.MinimumDevBuildDescriptionLength)]
    public string? Description { get; set; }
}
