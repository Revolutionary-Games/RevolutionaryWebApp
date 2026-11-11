namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Models;
using Services;
using Shared.Models;
using Shared.Models.Enums;

[ApiController]
[Route("api/v1/[controller]")]
public class UserActivityController : Controller
{
    private readonly ApplicationDbContext database;
    private readonly CustomMemoryCache cache;

    public UserActivityController(ApplicationDbContext database, CustomMemoryCache cache)
    {
        this.database = database;
        this.cache = cache;
    }

    [HttpGet("{userId:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<ActionResult<UserRecentActivityDTO[]>> Get(long userId)
    {
        var actor = HttpContext.AuthenticatedUserOrThrow();
        if (actor.Id != userId && !ModerationService.IsModerator(actor))
            return Forbid();
        if (!await database.Users.AnyAsync(user => user.Id == userId))
            return NotFound();

        var key = $"moderation-user-activity:{userId}";
        if (cache.Cache.TryGetValue(key, out UserRecentActivityDTO[]? cached))
            return cached!;

        var result = new List<UserRecentActivityDTO>();
        var comments = await database.TranslationComments.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.AuthorUserId == userId).OrderByDescending(item => item.CreatedAt).Take(100)
            .Select(item => new
            {
                item.CreatedAt, item.Key, item.LanguageCode, item.Deleted, item.Value.Language.Project.Slug,
            }).ToListAsync();
        result.AddRange(comments.Select(item => new UserRecentActivityDTO
        {
            CreatedAt = item.CreatedAt,
            Description = $"Comment on {item.Key}" + (item.Deleted ? " (deleted)" : string.Empty),
            Path = TranslationPath(item.Slug, item.LanguageCode, item.Key),
        }));
        var values = await database.TranslationValues.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.LastEditedByUserId == userId).OrderByDescending(item => item.LastEditedAt).Take(100)
            .Select(item => new { item.LastEditedAt, item.Key, item.LanguageCode, item.Language.Project.Slug })
            .ToListAsync();
        result.AddRange(values.Select(item => new UserRecentActivityDTO
        {
            CreatedAt = item.LastEditedAt ?? DateTime.UnixEpoch,
            Description = $"Edited translation {item.Key} ({item.LanguageCode})",
            Path = TranslationPath(item.Slug, item.LanguageCode, item.Key),
        }));
        var suggestions = await database.TranslationSuggestions.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.AuthorUserId == userId).OrderByDescending(item => item.CreatedAt).Take(100)
            .Select(item => new { item.CreatedAt, item.Key, item.LanguageCode, item.Value.Language.Project.Slug })
            .ToListAsync();
        result.AddRange(suggestions.Select(item => new UserRecentActivityDTO
        {
            CreatedAt = item.CreatedAt, Description = $"Suggested translation for {item.Key}",
            Path = TranslationPath(item.Slug, item.LanguageCode, item.Key),
        }));
        var terms = await database.DictionaryTerms.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.LastEditedByUserId == userId).OrderByDescending(item => item.UpdatedAt).Take(100)
            .Select(item => new { item.UpdatedAt, item.DictionaryId, item.Word }).ToListAsync();
        result.AddRange(terms.Select(item => new UserRecentActivityDTO
        {
            CreatedAt = item.UpdatedAt, Description = $"Edited dictionary term {item.Word}",
            Path = $"/translations/dictionaries/{item.DictionaryId}",
        }));
        var translatedTerms = await database.DictionaryTermTranslations.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.LastEditedByUserId == userId).OrderByDescending(item => item.UpdatedAt).Take(100)
            .Select(item => new { item.UpdatedAt, item.DictionaryId, item.Word, item.LanguageCode }).ToListAsync();
        result.AddRange(translatedTerms.Select(item => new UserRecentActivityDTO
        {
            CreatedAt = item.UpdatedAt, Description = $"Translated dictionary term {item.Word} ({item.LanguageCode})",
            Path = $"/translations/dictionaries/{item.DictionaryId}",
        }));
        var languageTerms = await database.DictionaryLanguageTerms.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.LastEditedByUserId == userId).OrderByDescending(item => item.UpdatedAt).Take(100)
            .Select(item => new { item.UpdatedAt, item.DictionaryId, item.Word, item.LanguageCode }).ToListAsync();
        result.AddRange(languageTerms.Select(item => new UserRecentActivityDTO
        {
            CreatedAt = item.UpdatedAt, Description = $"Edited language term {item.Word} ({item.LanguageCode})",
            Path = $"/translations/dictionaries/{item.DictionaryId}",
        }));
        var requests = await database.DictionaryTermRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.RequesterUserId == userId).OrderByDescending(item => item.CreatedAt).Take(100)
            .Select(item => new { item.CreatedAt, item.DictionaryId, item.Word }).ToListAsync();
        result.AddRange(requests.Select(item => new UserRecentActivityDTO
        {
            CreatedAt = item.CreatedAt, Description = $"Requested dictionary term {item.Word}",
            Path = $"/translations/dictionaries/{item.DictionaryId}",
        }));
        var files = await database.StorageItemVersions.AsNoTracking().Where(item => item.UploadedById == userId)
            .OrderByDescending(item => item.CreatedAt).Take(100)
            .Select(item => new { item.CreatedAt, item.StorageItemId }).ToListAsync();
        result.AddRange(files.Select(item => new UserRecentActivityDTO
        {
            CreatedAt = item.CreatedAt, Description = $"Uploaded file version for item {item.StorageItemId}",
            Path = "/files",
        }));
        var media = await database.MediaFiles.AsNoTracking().Where(item => item.UploadedById == userId ||
                item.LastModifiedById == userId).OrderByDescending(item => item.UpdatedAt).Take(100)
            .Select(item => new { item.UpdatedAt, item.Name, item.FolderId }).ToListAsync();
        result.AddRange(media.Select(item => new UserRecentActivityDTO
        {
            CreatedAt = item.UpdatedAt, Description = $"Uploaded or edited media {item.Name}",
            Path = "/media",
        }));
        var pages = await database.VersionedPages.AsNoTracking().Where(item => item.LastEditorId == userId)
            .OrderByDescending(item => item.UpdatedAt).Take(100)
            .Select(item => new { item.UpdatedAt, item.Title, item.Permalink }).ToListAsync();
        result.AddRange(pages.Select(item => new UserRecentActivityDTO
        {
            CreatedAt = item.UpdatedAt, Description = $"Edited page {item.Title}", Path = $"/{item.Permalink}",
        }));
        var ordered = result.OrderByDescending(item => item.CreatedAt).Take(100).ToArray();
        cache.Cache.Set(key, ordered, new MemoryCacheEntryOptions().SetSize(ordered.Length * 1000 + 1)
            .SetAbsoluteExpiration(TimeSpan.FromSeconds(30)));
        return ordered;
    }

    private static string TranslationPath(string slug, string language, string key)
    {
        return $"/translations/{slug}/{language}/edit?key={Uri.EscapeDataString(key)}";
    }
}
