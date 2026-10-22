# Project conventions

This guide records established patterns for adding or changing frontend pages and
components, with supporting server and realtime conventions. The examples were
chosen from existing, non-translation features. Translation code is intentionally
outside this guide while that area is being brought up to project standards.

## Frontend structure

- Put routed Blazor pages in `Client/Pages/` and reusable components in
  `Client/Shared/`. Keep server behavior in `Server/` and API data contracts in
  `Shared/`; frontend components should call the API rather than own persistence
  rules.
- Reuse an existing base/component when it fits. `SingleResourcePage<T,...>` and
  `ResourceEditorBase<T,...>` provide established fetch, notification, and edit
  flows for resource pages. `SingleResourceDisplayer` standardizes loading,
  missing/private resource, and error states. See the precompiled object page and
  editor in `Client/Pages/PrecompiledObject.razor` and
  `Client/Shared/SinglePageParts/PrecompiledObjectEditor.razor`.
- Use `AccessLimited` for conditional UI based on user access. This is a display
  rule only: the corresponding API endpoint must enforce access on the server as
  well. Follow established authorization attributes and checks in controllers.
- Use `HttpClient` with the shared JSON helpers for API calls. Show loading or
  processing state, surface request failures in the page (catch exceptions in all Http module calls),
  and give successful actions clear feedback. Keep forms in reusable components when they are shared
  by create and edit flows; `EditFormBase<T>` is the common parameter pattern.

## Data pages, sorting, and tables

`DataPage<T,TData>` in `Client/Shared/DataPage.cs` centralizes fetch state,
visible/background loading, errors, sorting, and delayed refresh after realtime
changes. Implement `StartQuery` to perform the API request and override the
notification add/update/delete hooks only when the default behavior is not
correct.

- Use `PaginatedPage<T>` when the API returns `PagedResult<T>`. It owns page and
  page-size parameters, adds them to requests, resets to page one when the sort
  column changes, replaces an updated row by ID, and schedules a page refetch for
  additions/deletions because those can change page membership.
- Use `ListDataPage<T>` when the API returns the full list. It replaces updated
  rows, removes deleted rows, and appends added rows. If appending would leave the
  collection incorrectly ordered, request a full refetch or override the hook.
- Use `SortHelper` for sort state and `TableColumn` metadata for the displayed
  columns. A column can provide a server-side sort property when its display name
  differs from the model property. Only mark columns sortable when the endpoint
  accepts that sort key.
- Use `PaginatedTable` for paginated results and `SortableTable` for full-list
  results. Pass `Columns`, `GetSortClass`, and the sort callback. Paginated pages
  also pass the page callback, page count/current page, loading overlay, and
  `NoItemsFound`. Render rows with `@key` when the model has a stable ID. The table
  components provide responsive styling, sorting affordances, loading states,
  empty-state text, and pagination.
- Keep search/filter state in query parameters when it should survive reloads or
  be shareable. Mark properties with `[QueryStringParameter]`, load them using
  `SetParametersFromQueryString(NavManager)` in `SetParametersAsync`, include
  non-default values in `CreatePageRequestParams`, prune default values, and
  update browser history through `ComponentUrlHelper.UpdateUrlHistoryIfChanged`
  from `OnQuerySent`. See `Client/Pages/UserList.razor` or
  `Client/Pages/Reports.razor` for paginated examples.
- Match the endpoint contract: list endpoints commonly require sort column,
  direction, page, and page size and return a `PagedResult<T>`. Preserve server
  validation and allowed sort fields when adding a sortable column.

## Buttons and forms

Use `Client/Shared/Button.razor` for actions. Its `ButtonStyle` selects the
Bootstrap style, `Small` adds the small size, `Enabled` controls disabled state,
`Spinner` shows progress, and `OnPressed` is the callback. Set `type="submit"`
for the established submit-button flow inside an `EditForm`; the component
validates the cascading `EditContext` before invoking the callback. Keep buttons
disabled while their action is processing to prevent duplicate requests. Use
`ChildContent` for the label and ordinary CSS classes for spacing/layout.

Build forms with `EditForm`, `DataAnnotationsValidator`, project input components
such as `CustomInputText`, and a `ValidationSummary` where useful. Give each input
a stable, unique `id` and matching label. For reusable create/edit forms, pass an
`EditContext`, model, processing state, status, and valid-submit callback as in
`PrecompiledObjectDataForm.razor`.

## Realtime notifications

The SignalR endpoint is mapped in `Server/Startup.cs` and implemented by
`Server/Hubs/NotificationsHub.cs`. The client connection is started once by
`Client/Program.cs`; feature components register handlers with the singleton
`NotificationHandler` in `Client/Services/NotificationHandler.cs`.

For model-driven notifications:

1. A server model implements `IUpdateNotifications` and returns notification /
   group-name pairs from `GetNotifications(EntityState)`. Use shared notification
   types from `Shared/Notifications/` and group names from
   `NotificationGroups`; use a list notification for collection changes and a
   model-updated notification for a single resource.
2. `ApplicationDbContext.SaveChanges` detects changed entities, asks the
   notification sender for notifications, saves first, then sends to the
   configured SignalR groups. Soft-delete models expose their delete status so
   the sender can report a soft-delete transition as an add or delete.
3. The hub checks authorization when a client joins each group. Update those
   checks in `NotificationsHub` whenever introducing a group, and do not treat a
   client-side access check as security enforcement.
4. A client component implements `INotificationHandler<TNotification>` and
   `GetWantedListenedGroups`. Register it when its data becomes available (usually
   during initialization, or after the first fetch when the group name depends
   on the resource ID), handle updates through `HandleItemNotification` or the
   single-resource base class, and unregister it during disposal. User-group
   dependent groups should be added only for the appropriate groups.

`NotificationHandler.Register` discovers every notification interface on the
component, and group memberships are recalculated when handlers or user info
change. Reconnection also rejoins the wanted groups. Keep client group names,
server notification targets, and hub authorization rules consistent.

## API and model boundaries

- Keep request/response types used by both client and server in `Shared/`.
  Maintain the DTO, endpoint, and client consumer together when changing an API
  contract.
- Follow the controller route and endpoint patterns already used by the feature.
  Validate input, use `AsNoTracking` for read-only queries, sort through the
  repository ordering helpers, and return the established result type. For
  paged lists, convert entities to lightweight info DTOs rather than serializing
  persistence models.
- Keep authorization on every server endpoint that needs it. Return the
  established `NotFound`, `Forbid`, or validation response when access or input
  is invalid; avoid exposing private resource existence through error details.
- If a model, `DbSet`, or EF configuration changes, follow the migration process
  in `AGENTS.md`. Save model changes through the configured database context so
  model-driven notifications can be emitted.

Useful existing references include `Client/Shared/DataPage.cs`,
`Client/Shared/PaginatedPage.cs`, `Client/Shared/SortableTable.razor`,
`Client/Shared/PaginatedTable.razor`, `Client/Shared/Button.razor`,
`Client/Services/NotificationHandler.cs`, `Server/Models/DevBuild.cs`,
`Server/Models/PrecompiledObject.cs`, `Server/Controllers/EmailBouncesController.cs`,
and `Server/Controllers/BaseSoftDeletedResourceController.cs`.

## Fontend request example

A complete example of how the frontend should make an API call.

First status display and button:
```razorhtmldialect
@inject HttpClient Http

<StatusMessageShower Message="@statusMessage" Type="@statusMessageType" OnClosed="@DismissActionMessage" />

<Button Enabled="@CanPerformAction" ButtonStyle="primary" Spinner="@processing"
        OnPressed="@(() => RerunBuild(true))">
    Rerun All
</Button>
```

And then the code implementation (note how we make sure the state change applies before the API call fires, 
and then afterwards we also make sure the state is cleared):

```csharp
    private bool processing;
    private string? statusMessage;
    private SiteNoticeType statusMessageType = SiteNoticeType.Danger;
    
    private CanPerformAction => !processing;

    private async void RerunBuild(bool all)
    {
        processing = true;
        statusMessage = null;
        statusMessageType = SiteNoticeType.Danger;
        await InvokeAsync(StateHasChanged);

        HttpResponseMessage result;

        try
        {
            result = await Http.PostAsJsonAsync("api/v1/someApiPath", new SomeObjectDTO(...));
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
            statusMessage = "Write here what succeeded";
            statusMessageType = SiteNoticeType.Info;
        }

        await InvokeAsync(StateHasChanged);
    }
    
    private void DismissActionMessage()
    {
        createMessage = null;
        StateHasChanged();
    }
```
