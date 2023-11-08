namespace ThriveDevCenter.Client.Shared;

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Services;
using ThriveDevCenter.Shared;
using ThriveDevCenter.Shared.Notifications;

public abstract class ResourceEditorBase<T, TNotification, TID> : SingleResourcePage<T, TNotification, TID>
    where T : class, IIdentifiable, new()
    where TNotification : ModelUpdated<T>
    where TID : IEquatable<TID>
{
    protected bool processing;
    protected string? statusMessage;
    protected SiteNoticeType statusMessageType = SiteNoticeType.Danger;

    protected EditContext editContext = null!;

    protected T? editedData;

    [Parameter]
    [EditorRequired]
    public int ColumnSpan { get; set; }

    protected abstract string ElementIdPrefix { get; }

    [Inject]
    protected NotificationHandler NotificationHandler { get; private set; } = null!;

    [Inject]
    protected HttpClient Http { get; private set; } = null!;

    public async ValueTask DisposeAsync()
    {
        await NotificationHandler.Unregister(this);
    }

    protected override async Task OnFirstDataReceived()
    {
        await NotificationHandler.Register(this);

        editedData = CreateEditData(Data ?? throw new Exception("Failed to receive data for edit"));

        statusMessage = null;

        editContext = new EditContext(editedData!);
    }

    protected abstract T CreateEditData(T serverData);

    protected abstract string GetSaveEndpoint(T serverData);

    protected async Task Save()
    {
        processing = true;
        statusMessage = null;
        statusMessageType = SiteNoticeType.Danger;
        await InvokeAsync(StateHasChanged);

        HttpResponseMessage result;

        try
        {
            result = await Http.PutAsJsonAsync(GetSaveEndpoint(Data!), editedData);
        }
        catch (HttpRequestException e)
        {
            statusMessage = $"Network request failed: {e.Message}";
            processing = false;
            await InvokeAsync(StateHasChanged);
            return;
        }

        processing = false;

        if (!result.IsSuccessStatusCode)
        {
            var content = await result.Content.ReadAsStringAsync();

            statusMessage = $"Error, server responded with: {content}, {result.StatusCode}";
        }
        else
        {
            statusMessage = "Changes saved.";
            statusMessageType = SiteNoticeType.Primary;
        }

        await InvokeAsync(StateHasChanged);
    }

    protected void HideStatusMessage()
    {
        statusMessage = null;
    }
}
