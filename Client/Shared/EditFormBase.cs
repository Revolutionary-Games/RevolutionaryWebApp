namespace RevolutionaryWebApp.Client.Shared;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using RevolutionaryWebApp.Shared;

/// <summary>
///   Base class for edit forms
/// </summary>
/// <typeparam name="T">The type of object to edit</typeparam>
public class EditFormBase<T> : ComponentBase
{
    [Parameter]

    // ReSharper disable once InconsistentNaming
#pragma warning disable SA1300
    public string? @class { get; set; }
#pragma warning restore SA1300

#pragma warning disable CS8618
    [Parameter]
    [EditorRequired]
    public EditContext EditContext { get; set; }

    [Parameter]
    [EditorRequired]
    public T EditedData { get; set; }

    [Parameter]
    [EditorRequired]
    public string ControlIdSuffix { get; set; }

#pragma warning restore CS8618

    [Parameter]
    public string? StatusMessage { get; set; }

    [Parameter]
    public SiteNoticeType StatusMessageType { get; set; } = SiteNoticeType.Danger;

    [Parameter]
    public bool Processing { get; set; }

    [Parameter]
    [EditorRequired]
    public EventCallback OnValidSubmit { get; set; }

    [Parameter]
    public string? OverrideSaveText { get; set; }

    protected string SaveButtonText => string.IsNullOrEmpty(OverrideSaveText) ? "Save Changes" : OverrideSaveText;
}
