# Project conventions

This guide records established patterns for adding or changing frontend pages,
components, and server controllers, with supporting model and realtime
conventions.

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

## Controllers

Use `[ApiController]` and `[Route("api/v1/[controller]")]` for ordinary API
controllers. Keep the controller class name aligned with its route. Use a
custom route only when the public API intentionally has a different shape, and
preserve existing route shapes when refactoring a controller.

For resources with reversible deletion, derive from
`BaseSoftDeletedResourceController<TModel,TInfo,TDTO>`. It already provides:

- `GET /api/v1/{controller}` as a paged list, with sort column, direction, page,
  page size, and an optional `deleted` filter;
- `GET /api/v1/{controller}/{id}` as a single-resource read;
- `DELETE /api/v1/{controller}/{id}` as a soft delete; and
- `POST /api/v1/{controller}/{id}/restore` as a restore.

The base controller applies the configured view-access check, hides deleted
items from ordinary reads, restricts deleted-item reads to admins, and uses
`AsNoTracking` for its default list query. Override `CheckExtraAccess` for
resource-specific access rules or `GetEntitiesForJustInfo` when the default
list query needs additional filtering. Implement `SaveResourceChanges` to bump
the timestamp and save through the notification-enabled context.

The model used with this base class implements `ISoftDeletable`,
`IDTOCreator<TDTO>`, and `IInfoCreator<TInfo>`. Keep the list `Info` type small;
use the full `DTO` type for single-resource reads and writes. Model changes must
be saved through the configured context so `IUpdateNotifications` events are
sent. Use `UpdateableModel` for ordinary ID-based entities with creation and
update timestamps.

Mark only supported sort properties with `[AllowSortingBy]`. The shared
`OrderBy` helper rejects arbitrary client-supplied property names; key properties
are allowed automatically. When writing a custom list action, use
`AsNoTracking`, the repository `OrderBy` helper, and `ToPagedResultAsync`, then
map each row to its `Info` DTO. Catch invalid sort requests and follow the
existing warning and `HttpResponseException` pattern. Do not sort by a property
that has not been explicitly allowed.

For updates, mark each writable model property with
`[UpdateFromClientRequest]` and keep a matching property on the request DTO.
`ModelUpdateApplyHelper.ApplyUpdateRequestToModel` applies only marked
properties and returns whether anything changed, a description for audit/logging,
and the changed field names. Return early when no fields changed. For real
updates, validate any cross-field or uniqueness rules, call `BumpUpdatedAt()`,
save an `AdminAction` when the operation is administrative, save the entity, and
write an information log after the save succeeds. Map create requests explicitly
to a new model; do not bind persistence entities directly from request bodies.

Keep authorization on the server with the established filters or explicit
checks. A UI access check does not protect an endpoint. Reuse the authenticated
user within a method (`var user = HttpContext.AuthenticatedUserOrThrow();`)
instead of fetching it repeatedly. For important successful actions, use the
controller's `ILogger<T>` with structured fields such as resource ID and actor;
avoid logging secrets or user-submitted text. Use `NotFound`, `Forbid`,
`BadRequest`, or `Conflict` consistently with the existing controller behavior.

This `Banana` example assumes `Banana` inherits `UpdateableModel`, implements
`ISoftDeletable`, `IUpdateNotifications`, `IDTOCreator<BananaDTO>`, and
`IInfoCreator<BananaInfo>`, and marks `Name` with `[AllowSortingBy]` and
`[UpdateFromClientRequest]`. `BananaInfo` is the compact list response;
`BananaDTO` is the detail response and create/update request. The inherited
actions provide list, get, soft delete, and restore; this controller adds create
and update:

```csharp
namespace RevolutionaryWebApp.Server.Controllers;

using System.Threading.Tasks;
using Authorization;
using DevCenterCommunication.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class BananasController : BaseSoftDeletedResourceController<Banana, BananaInfo, BananaDTO>
{
    private readonly NotificationsEnabledDb database;

    public BananasController(ILogger<BananasController> logger, NotificationsEnabledDb database)
    {
        Logger = logger;
        this.database = database;
    }

    protected override ILogger Logger { get; }
    protected override DbSet<Banana> Entities => database.Bananas;
    protected override GroupType RequiredViewAccessLevel => GroupType.NotLoggedIn;

    [HttpPost]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<BananaDTO>> Create([FromBody] BananaDTO request)
    {
        var name = request.Name.Trim();
        if (await database.Bananas.AnyAsync(banana => banana.Name == name))
            return Conflict("A banana with that name already exists");

        var user = HttpContext.AuthenticatedUserOrThrow();
        var banana = new Banana { Name = name };
        await Entities.AddAsync(banana);
        await database.AdminActions.AddAsync(new AdminAction($"Banana {banana.Name} created")
        {
            PerformedById = user.Id,
        });
        await database.SaveChangesAsync();

        Logger.LogInformation("Banana {Id} ({Name}) created by {Email}", banana.Id, banana.Name, user.Email);
        return CreatedAtAction(nameof(GetSingle), new { id = banana.Id }, banana.GetDTO());
    }

    [HttpPut("{id:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> Update(long id, [FromBody] BananaDTO request)
    {
        if (request.Id != id)
            return BadRequest("ID in the request does not match the URL");

        var banana = await Entities.FindAsync(id);
        if (banana == null || banana.Deleted)
            return NotFound();

        request.Name = request.Name.Trim();
        if (await database.Bananas.AnyAsync(other => other.Name == request.Name && other.Id != id))
            return Conflict("A banana with that name already exists");

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(banana, request);
        if (!changes)
            return Ok();

        var user = HttpContext.AuthenticatedUserOrThrow();
        banana.BumpUpdatedAt();
        await database.AdminActions.AddAsync(new AdminAction($"Banana {id} edited", description)
        {
            PerformedById = user.Id,
        });
        await database.SaveChangesAsync();

        Logger.LogInformation("Banana {Id} edited by {Email}, changes: {Description}", id, user.Email, description);
        return Ok();
    }

    protected override async Task SaveResourceChanges(Banana banana)
    {
        var user = HttpContext.AuthenticatedUserOrThrow();
        banana.BumpUpdatedAt();
        var action = banana.Deleted ? "deleted" : "restored";
        await database.AdminActions.AddAsync(new AdminAction($"Banana {banana.Id} {action}")
        {
            PerformedById = user.Id,
        });
        await database.SaveChangesAsync();
        Logger.LogInformation("Banana {Id} {Action} by {Email}", banana.Id, action, user.Email);
    }
}
```

The resulting endpoints are `GET /api/v1/Bananas`,
`GET /api/v1/Bananas/{id}`, `POST /api/v1/Bananas`,
`PUT /api/v1/Bananas/{id}`, and `DELETE /api/v1/Bananas/{id}`. The list query
requires `sortColumn`, `sortDirection`, `page`, and `pageSize`. The delete
endpoint marks the row deleted; it does not remove the database row.

For implementation details and additional cases, see
`Server/Controllers/BaseSoftDeletedResourceController.cs`,
`Server/Utilities/ModelUpdateApplyHelper.cs`,
`Shared/AllowSortingByAttribute.cs`, `Shared/LinqHelpers.cs`, and
`Server/Controllers/PrecompiledObjectController.cs`.

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

<StatusMessageShower Message="@statusMessage" Type="@statusMessageType" OnClosed="@DismissActionMessage"/>

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

If using this alternative format, make sure to also mark state as changed at the end:
```csharp
private async Task CreateToken()
{
    creating = true;
    message = null;
    await InvokeAsync(StateHasChanged);

    try
    {
        using var response = await Http.PostAsJsonAsync("api/v1/GithubConfiguration/tokens", newToken);
        if (!response.IsSuccessStatusCode)
        {
            message = "Could not create the GitHub token. The selected purpose may already have a token.";
            messageType = SiteNoticeType.Danger;
            return;
        }

        message = $"{newToken.Purpose} token created.";
        messageType = SiteNoticeType.Success;
    }
    catch (Exception e)
    {
        message = $"Error while creating the GitHub token: {e.Message}";
        messageType = SiteNoticeType.Danger;
    }
    finally
    {
        creating = false;
        await InvokeAsync(StateHasChanged);
    }
}
```
