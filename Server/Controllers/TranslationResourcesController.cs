namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Interfaces;
using Models.Translations;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

/// <summary>
///   Administrative deletion and recovery, including resources with composite keys.
/// </summary>
[ApiController]
[Route("api/v1/admin/[controller]")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
public class TranslationResourcesController : Controller
{
    private readonly NotificationsEnabledDb database;
    private readonly ILogger<TranslationResourcesController> logger;

    public TranslationResourcesController(NotificationsEnabledDb database,
        ILogger<TranslationResourcesController> logger)
    {
        this.database = database;
        this.logger = logger;
    }

    [HttpGet("{kind}")]
    public async Task<ActionResult<PagedResult<SoftDeletedResource>>> List(TranslationResourceKind kind,
        bool deleted = false, string sortColumn = "UpdatedAt", SortDirection sortDirection = SortDirection.Descending,
        int page = 1, int pageSize = 20)
    {
        if (page < 1 || pageSize is < 1 or > 50 ||
            sortColumn is not ("UpdatedAt" or "Name" or "Id"))
        {
            return BadRequest("Invalid pagination or sort");
        }

        return kind switch
        {
            TranslationResourceKind.Projects =>
                await Query<TranslationProject>(deleted, sortColumn, sortDirection, page, pageSize),
            TranslationResourceKind.Languages =>
                await Query<TranslationProjectLanguage>(deleted, sortColumn, sortDirection, page, pageSize),
            TranslationResourceKind.Dictionaries =>
                await Query<TranslationDictionary>(deleted, sortColumn, sortDirection, page, pageSize),
            TranslationResourceKind.Terms =>
                await Query<DictionaryTerm>(deleted, sortColumn, sortDirection, page, pageSize),
            TranslationResourceKind.LanguageTerms =>
                await Query<DictionaryLanguageTerm>(deleted, sortColumn, sortDirection, page, pageSize),
            _ => BadRequest("Unknown resource kind"),
        };
    }

    [HttpPost("{kind}/{key}/delete")]
    public Task<IActionResult> Delete(TranslationResourceKind kind, string key)
    {
        return SetDeleted(kind, key, true);
    }

    [HttpPost("{kind}/{key}/restore")]
    public Task<IActionResult> Restore(TranslationResourceKind kind, string key)
    {
        return SetDeleted(kind, key, false);
    }

    private static Expression<Func<T, object>> CreateSortSelector<T>(string propertyName)
    {
        var parameter = Expression.Parameter(typeof(T), "item");
        var property = Expression.Property(parameter, propertyName);
        return Expression.Lambda<Func<T, object>>(Expression.Convert(property, typeof(object)), parameter);
    }

    private async Task<PagedResult<SoftDeletedResource>> Query<T>(bool deleted, string sortColumn,
        SortDirection direction, int page, int pageSize)
        where T : class, ISoftDeletable
    {
        string nameProperty;
        if (typeof(T) == typeof(TranslationProjectLanguage))
        {
            nameProperty = "LanguageCode";
        }
        else
        {
            nameProperty = typeof(T) == typeof(DictionaryTerm) || typeof(T) == typeof(DictionaryLanguageTerm) ?
                "Word" :
                "Name";
        }

        var primaryKey = database.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!;
        var sortProperty = sortColumn switch
        {
            "UpdatedAt" => "UpdatedAt",
            "Id" => primaryKey.Properties[0].Name,
            _ => nameProperty,
        };
        var query = database.Set<T>().IgnoreQueryFilters().AsNoTracking().Where(item => item.Deleted == deleted);
        var ordered = direction == SortDirection.Descending ?
            query.OrderByDescending(CreateSortSelector<T>(sortProperty)) :
            query.OrderBy(CreateSortSelector<T>(sortProperty));

        // Include every primary-key field to keep pagination stable when timestamps or names match.
        foreach (var property in primaryKey.Properties)
            ordered = ordered.ThenBy(CreateSortSelector<T>(property.Name));
        var result = await ordered.ToPagedResultAsync(page, pageSize);
        return result.ConvertResult(item =>
        {
            var values = primaryKey.Properties.Select(property => property.PropertyInfo!.GetValue(item)).ToArray();
            return new SoftDeletedResource
            {
                Id = primaryKey.Properties.Count == 1 ?
                    (long)values[0]! :
                    (long)(page - 1) * pageSize + Array.IndexOf(result.Results.ToArray(), item) + 1,
                Name = $"{ResourceName(item)} ({string.Join(", ", values)})",
                UpdatedAt = (DateTime)typeof(T).GetProperty("UpdatedAt")!.GetValue(item)!,
                ResourceKey = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))),
            };
        });

        string ResourceName(T item)
        {
            return typeof(T).GetProperty(nameProperty)!.GetValue(item)?.ToString() ?? string.Empty;
        }
    }

    private async Task<IActionResult> SetDeleted(TranslationResourceKind kind, string key, bool deleted)
    {
        var type = kind switch
        {
            TranslationResourceKind.Projects => typeof(TranslationProject),
            TranslationResourceKind.Languages => typeof(TranslationProjectLanguage),
            TranslationResourceKind.Dictionaries => typeof(TranslationDictionary),
            TranslationResourceKind.Terms => typeof(DictionaryTerm),
            TranslationResourceKind.LanguageTerms => typeof(DictionaryLanguageTerm),
            _ => null,
        };
        if (type == null)
            return NotFound();

        object?[] values;
        try
        {
            var elements =
                JsonSerializer.Deserialize<JsonElement[]>(Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(key)))!;
            var properties = database.Model.FindEntityType(type)!.FindPrimaryKey()!.Properties;
            if (elements.Length != properties.Count)
                return BadRequest("Invalid resource key");

            // This line is slightly suspicious, but only usable by admins
            values = elements.Select((element, index) => element.Deserialize(properties[index].ClrType)).ToArray();
            if (values.Any(value => value == null))
                return BadRequest("Invalid resource key");
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException)
        {
            return BadRequest("Invalid resource key");
        }

        await using var projectLock = type == typeof(TranslationProject) || type == typeof(TranslationProjectLanguage) ?
            await TranslationProjectAdvisoryLock.AcquireAsync(database, (long)values[0]!, HttpContext.RequestAborted) :
            null;

        // Find bypasses query filters intentionally: recovery must also find deleted resources.
        var resource = await database.FindAsync(type, values);
        if (resource is not ISoftDeletable item)
            throw new InvalidOperationException("Found resource is not soft-deletable");

        if (item.Deleted == deleted)
            return BadRequest(deleted ? "Resource is already deleted" : "Resource is not deleted");

        if (resource is TranslationProjectLanguage language && deleted)
        {
            var project = await database.TranslationProjects.IgnoreQueryFilters()
                .SingleAsync(project => project.Id == language.ProjectId);
            if (language.LanguageCode == project.SourceLanguage)
                return BadRequest("The source language cannot be deleted separately from its project");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();

        item.Deleted = deleted;
        ((IHasUpdatedAt)resource).BumpUpdatedAt();
        database.AdminActions.Add(new AdminAction($"Translation {kind} {key} {(deleted ? "deleted" : "restored")}")
        {
            PerformedById = user.Id,
        });

        logger.LogInformation("Translation resource {Kind} {Key} {Operation} by {Email}", kind, key,
            deleted ? "deleted" : "restored", user.Email);

        await database.SaveChangesAsync();
        return Ok();
    }
}
