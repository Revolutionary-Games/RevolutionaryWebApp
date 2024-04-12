namespace RevolutionaryWebApp.Client.Shared.CMS;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using RevolutionaryWebApp.Shared;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Models.Pages;
using RevolutionaryWebApp.Shared.Notifications;
using Services;

/// <summary>
///   Base class for all single page view editors (posts, pages, wiki pages)
/// </summary>
public abstract class EditablePageView : SingleResourcePage<VersionedPageDTO, VersionedPageUpdated, long>,
    INotificationHandler<PageEditNotice>, IAsyncDisposable
{
    protected SiteNoticeType saveMessageType = SiteNoticeType.Danger;
    protected string? saveMessage;
    protected bool processingSave;

    protected bool versionConflict;
    protected int? editedVersion;

    protected DateTime? someoneElseEditTime;
    protected long? someoneElseEditId;

    private const string ErrorAboutSendingEditMessage =
        "Failed to send notice to other users through the server that this page is being edited";

    private string? userEditedContent;
    private bool editedProperties;
    private bool editedText;

    private DateTime? lastReportedEditing;
    private Timer checkEditConflictTimer;

    public EditablePageView()
    {
        checkEditConflictTimer = new Timer(OnTimer, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
    }

    protected abstract PageType EditedPageType { get; }

    protected bool CanSaveChanges => HasEditedSomething && !processingSave;

    /// <summary>
    ///   True once something is edited, enables the save button.
    /// </summary>
    protected bool HasEditedSomething => editedProperties || editedText;

    protected string? EditableContent
    {
        get => userEditedContent ?? Data?.LatestContent;
        set
        {
            if (value == Data?.LatestContent)
            {
                // Resetting edit status
                editedText = false;
                userEditedContent = null;
            }
            else
            {
                userEditedContent = value;

                if (!editedText)
                {
                    editedText = true;
                    StateHasChanged();
                }
            }
        }
    }

    public override void GetWantedListenedGroups(IUserGroupData currentUserGroups, ISet<string> groups)
    {
        switch (EditedPageType)
        {
            case PageType.Template:
                groups.Add(NotificationGroups.PageTemplateUpdatedPrefix + Id);
                break;
            case PageType.NormalPage:
                groups.Add(NotificationGroups.PageUpdatedPrefix + Id);
                break;
            case PageType.Post:
                groups.Add(NotificationGroups.PostUpdatedPrefix + Id);
                break;
            case PageType.WikiPage:
                groups.Add(NotificationGroups.WikiPageUpdatedPrefix + Id);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public async Task Handle(PageEditNotice notification, CancellationToken cancellationToken)
    {
        if (Data == null)
            return;

        if (notification.PageId == Data.Id)
        {
            // Got notice that someone else is editing this page, show a warning
            someoneElseEditId = notification.EditorUserId;
            someoneElseEditTime = DateTime.Now;
            await InvokeAsync(StateHasChanged);
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        await checkEditConflictTimer.DisposeAsync();
    }

    protected abstract string GetPageEndpoint();

    protected override bool OnUpdateNotificationReceived(VersionedPageDTO newData)
    {
        if (newData.Deleted)
        {
            OnDeleted();
            return true;
        }

        if (Data != null && Data.LatestContent != newData.LatestContent)
        {
            // Let the user know that some bad stuff is going to happen if they keep editing
            versionConflict = true;
            StateHasChanged();
        }

        // Allow the data to change (the main body editor is made so that it doesn't lose the changes)
        return true;
    }

    protected override Task<VersionedPageDTO?> StartQuery()
    {
        return AccessHttp().GetFromJsonAsync<VersionedPageDTO>(GetPageEndpoint());
    }

    protected void OnEdited(string text)
    {
        // When first editing something the version number is locked in of the object to detect later if there is an
        // edit conflict
        if (Data != null)
            editedVersion ??= Data.VersionNumber;

        EditableContent = text;

        SendNoticeAboutEditingPage();
    }

    protected abstract HttpClient AccessHttp();

    protected void OnPropertiesChanged()
    {
        if (!editedProperties)
        {
            editedProperties = true;
            StateHasChanged();
        }

        SendNoticeAboutEditingPage();
    }

    protected async void OnDeleted()
    {
        if (Data != null)
        {
            Data.Deleted = true;
        }

        if (!processingSave)
        {
        }

        AccessHttp();

        await InvokeAsync(StateHasChanged);
    }

    protected async void OnRestored()
    {
        if (Data != null)
        {
            Data.Deleted = false;
        }

        await InvokeAsync(StateHasChanged);
    }

    protected void SuppressEditWarning()
    {
        versionConflict = false;
        StateHasChanged();
    }

    protected void ClearSaveMessage()
    {
        saveMessage = null;
    }

    protected async Task SaveChanges()
    {
        if (Data == null)
        {
            saveMessage = "No data object";
            await InvokeAsync(StateHasChanged);
            return;
        }

        processingSave = true;
        saveMessage = null;
        saveMessageType = SiteNoticeType.Danger;

        HttpResponseMessage result;

        // Prepare data from the DTO and our custom fields that are used to not get remote edits overriding all local
        // changes
        var dataToSend = Data.Clone();
        dataToSend.LatestContent = EditableContent ?? string.Empty;
        dataToSend.VersionNumber = editedVersion ?? dataToSend.VersionNumber;

        if (!string.IsNullOrEmpty(Data.LatestContent) && string.IsNullOrWhiteSpace(dataToSend.LatestContent))
        {
            saveMessage = "Cannot set new page content to blank when it wasn't blank previously";
            return;
        }

        await InvokeAsync(StateHasChanged);

        try
        {
            result = await AccessHttp().PutAsJsonAsync(GetPageEndpoint(), dataToSend);
        }
        catch (HttpRequestException e)
        {
            saveMessage = $"Network request failed: {e.Message}";
            processingSave = false;
            await InvokeAsync(StateHasChanged);
            return;
        }

        processingSave = false;

        if (!result.IsSuccessStatusCode)
        {
            var content = await result.Content.ReadAsStringAsync();

            saveMessage = $"Error saving. If there is a version conflict please open a new tab and copy your changes " +
                $"there before attempting saving again. server responded with: {content}, {result.StatusCode}";
        }
        else
        {
            saveMessage = "Changes saved successfully";
            saveMessageType = SiteNoticeType.Primary;
            editedProperties = false;
            editedText = false;
            userEditedContent = null;
        }

        await InvokeAsync(StateHasChanged);
    }

    private async void SendNoticeAboutEditingPage()
    {
        if (processingSave)
            return;

        var now = DateTime.Now;

        if (lastReportedEditing == null || now - lastReportedEditing >= AppInfo.TimeBetweenReportEditingPage)
        {
            lastReportedEditing = now;

            HttpResponseMessage result;

            try
            {
                result = await AccessHttp().GetAsync($"api/v1/EditNotifications?pageId={Id}");
            }
            catch (HttpRequestException)
            {
                saveMessageType = SiteNoticeType.Warning;
                saveMessage = ErrorAboutSendingEditMessage;
                await InvokeAsync(StateHasChanged);
                return;
            }

            if (!result.IsSuccessStatusCode)
            {
                saveMessageType = SiteNoticeType.Warning;
                saveMessage = ErrorAboutSendingEditMessage;
                await InvokeAsync(StateHasChanged);
            }
            else
            {
                // Stop showing the error if the condition went away
                if (saveMessage == ErrorAboutSendingEditMessage)
                {
                    saveMessage = null;
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
    }

    private async void OnTimer(object? state)
    {
        if (someoneElseEditTime != null)
        {
            var now = DateTime.Now;

            if (now - someoneElseEditTime > AppInfo.OtherUserEditStaleAfter)
            {
                // Assumed that other person is no longer editing this page, so stop the warning
                await InvokeAsync(() =>
                {
                    someoneElseEditId = null;
                    someoneElseEditTime = null;
                    StateHasChanged();
                });
            }
        }
    }
}
