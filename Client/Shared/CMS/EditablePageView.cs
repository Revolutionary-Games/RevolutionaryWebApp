namespace RevolutionaryWebApp.Client.Shared.CMS;

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Components.Rendering;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Models.Pages;
using RevolutionaryWebApp.Shared.Notifications;

/// <summary>
///   Base class for all single page view editors (posts, pages, wiki pages)
/// </summary>
public abstract class EditablePageView : SingleResourcePage<VersionedPageDTO, VersionedPageUpdated, long>
{
    // TODO: set to false
    protected bool versionConflict = true;

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
        versionConflict = true;
        StateHasChanged();
        return false;
    }

    protected void EditConflictNotice(RenderTreeBuilder builder)
    {
        if (!versionConflict)
            return;

        // TODO: ignore when current user edited page properties
        builder.AddComponentParameter(0, nameof(StatusMessageShower.Message),
            "This page has been modified. To safely save your changes open a new tab to this page and copy your " +
            "changes there.");
        builder.OpenComponent<StatusMessageShower>(0);
        builder.CloseComponent();
    }
}
