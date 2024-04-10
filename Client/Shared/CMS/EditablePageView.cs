namespace RevolutionaryWebApp.Client.Shared.CMS;

using System;
using System.Collections.Generic;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Models.Pages;
using RevolutionaryWebApp.Shared.Notifications;

/// <summary>
///   Base class for all single page view editors (posts, pages, wiki pages)
/// </summary>
public abstract class EditablePageView : SingleResourcePage<VersionedPageDTO, VersionedPageUpdated, long>
{
    protected bool versionConflict;

    protected abstract PageType EditedPageType { get; }

    // TODO: would be nice to be able to show a warning to other users if someone else is editing the same page

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

    protected override bool OnUpdateNotificationReceived(VersionedPageDTO newData)
    {
        if (Data != null && Data.LatestContent != newData.LatestContent)
        {
            // Let the user know that some bad stuff is going to happen if they keep editing
            versionConflict = true;
            StateHasChanged();
        }

        // Allow the data to change (the main body editor is made so that it doesn't lose the changes)
        return true;
    }

    protected void SuppressEditWarning()
    {
        versionConflict = false;
        StateHasChanged();
    }
}