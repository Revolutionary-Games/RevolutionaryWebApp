namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Translations;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class TranslationDictionariesController : Controller
{
    private readonly ILogger<TranslationDictionariesController> logger;
    private readonly NotificationsEnabledDb database;

    public TranslationDictionariesController(ILogger<TranslationDictionariesController> logger,
        NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    [HttpGet]
    public async Task<ActionResult<TranslationDictionaryDTO[]>> List()
    {
        return await database.TranslationDictionaries.AsNoTracking().OrderBy(d => d.Name)
            .Select(d => new TranslationDictionaryDTO { Id = d.Id, Name = d.Name })
            .ToArrayAsync();
    }

    [HttpGet("{id:long}/languages")]
    public async Task<ActionResult<string[]>> Languages(long id)
    {
        // TODO: should ordering be the same as for languages which use the name of a language for sorting?
        var languages = await database.TranslationProjects.Where(p => p.DictionaryId == id)
            .Join(database.TranslationProjectLanguages, p => p.Id, l => l.ProjectId,
                (p, l) => l.LanguageCode).Distinct().OrderBy(code => code).ToArrayAsync();
        return languages;
    }

    [HttpPost]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<TranslationDictionaryDTO>> Create([FromBody] CreateDictionaryRequest request)
    {
        var dictionary = new TranslationDictionary { Name = request.Name.Trim() };
        if (await database.TranslationDictionaries.AnyAsync(d => d.Name == dictionary.Name))
            return Conflict("Dictionary name already exists");

        await database.TranslationDictionaries.AddAsync(dictionary);
        await database.SaveChangesAsync();
        logger.LogInformation("Translation dictionary {DictionaryId} ({Name}) created by {Email}", dictionary.Id,
            dictionary.Name, HttpContext.AuthenticatedUserOrThrow().Email);
        return CreatedAtAction(nameof(List), dictionary.GetDTO());
    }

    [HttpGet("{id:long}/terms")]
    public async Task<ActionResult<PagedResult<DictionaryTermDTO>>> Terms(long id,
        [FromQuery] string language = "en", [Required] string sortColumn = "Word",
        [Required] SortDirection sortDirection = SortDirection.Ascending,
        [Required] [Range(1, int.MaxValue)] int page = 1,
        [Required] [Range(1, 100)] int pageSize = 100)
    {
        var sourceTerms = database.DictionaryTerms.AsNoTracking().Where(t => t.DictionaryId == id)
            .Select(term => new DictionaryTermDTO
            {
                DictionaryId = id,
                Word = term.Word,
                SourceWord = null,
                Context = term.Context,
                SourceExplanation = term.Explanation,
                TargetText = string.Empty,
                State = TranslationState.NeedsTranslation,
                SourceLanguageCode = "en",
                IsLanguageSpecific = false,
            });
        var languageTerms = database.DictionaryLanguageTerms.AsNoTracking()
            .Where(t => t.DictionaryId == id && t.LanguageCode == language)
            .Select(term => new DictionaryTermDTO
            {
                DictionaryId = id,
                Word = term.Word,
                SourceWord = term.SourceWord,
                Context = term.Context,
                SourceExplanation = term.Explanation,
                TargetText = term.Word,
                State = TranslationState.Translated,
                SourceLanguageCode = term.LanguageCode,
                IsLanguageSpecific = true,
            });

        var termsQuery = sourceTerms.Concat(languageTerms);
        if (sortColumn != nameof(DictionaryTermDTO.Word) && sortColumn != nameof(DictionaryTermDTO.Context))
            return BadRequest("Invalid sort column");

        var tieBreakers = new[]
        {
            nameof(DictionaryTermDTO.Word),
            nameof(DictionaryTermDTO.Context),
            nameof(DictionaryTermDTO.SourceLanguageCode),
        };
        var orderedTerms = termsQuery.OrderBy(sortColumn, sortDirection, tieBreakers);
        var result = await orderedTerms.ToPagedResultAsync(page, pageSize);

        HttpContext.RequestAborted.ThrowIfCancellationRequested();

        var pageTerms = result.Results.Where(t => !t.IsLanguageSpecific).ToArray();
        var words = pageTerms.Select(t => t.Word).Distinct().ToArray();
        var contexts = pageTerms.Select(t => t.Context).Distinct().ToArray();
        var targets = await database.DictionaryTermTranslations.AsNoTracking().Where(t =>
            t.DictionaryId == id && t.LanguageCode == language && words.Contains(t.Word) &&
            contexts.Contains(t.Context)).ToListAsync();
        var targetMap = targets.ToDictionary(t => (t.Word, t.Context));
        foreach (var term in pageTerms)
        {
            targetMap.TryGetValue((term.Word, term.Context), out var target);
            term.TargetExplanation = target?.Explanation;
            term.TargetText = target?.Text ?? string.Empty;
            term.State = target?.State ?? TranslationState.NeedsTranslation;
            term.LastEditedByUserId = target?.LastEditedByUserId;
        }

        return result;
    }

    [OutputCache(PolicyName = "ShortLived")]
    [HttpGet("matching")]
    public async Task<ActionResult<DictionaryTermDTO[]>> Matching(long projectId, string key, string language,
        CancellationToken cancellationToken)
    {
        var project = await database.TranslationProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.Enabled, cancellationToken);
        var unit = await database.TranslationUnits.AsNoTracking()
            .FirstOrDefaultAsync(u => u.ProjectId == projectId && u.Key == key, cancellationToken);
        if (project?.DictionaryId == null || unit == null)
            return Array.Empty<DictionaryTermDTO>();

        // TODO: If this scan becomes expensive as dictionaries grow, cache matching terms or computed matches in
        // Redis and invalidate the cache when a dictionary entry or the source translation text changes.
        var terms = await database.DictionaryTerms.AsNoTracking()
            .Where(t => t.DictionaryId == project.DictionaryId)
            .ToListAsync(cancellationToken);
        var matchingTerms = terms.Where(term => DictionaryWordMatches(unit.SourceText, term.Word)).ToList();
        var languageTerms = await database.DictionaryLanguageTerms.AsNoTracking()
            .Where(term => term.DictionaryId == project.DictionaryId && term.LanguageCode == language &&
                term.SourceWord != string.Empty)
            .ToListAsync(cancellationToken);
        var matchingLanguageTerms = languageTerms
            .Where(term => DictionaryWordMatches(unit.SourceText, term.SourceWord)).ToList();

        var targetMap = new Dictionary<(string Word, string Context), DictionaryTermTranslation>();
        if (matchingTerms.Count > 0)
        {
            var words = matchingTerms.Select(term => term.Word).Distinct().ToArray();
            var contexts = matchingTerms.Select(term => term.Context).Distinct().ToArray();
            var target = await database.DictionaryTermTranslations.AsNoTracking()
                .Where(term => term.DictionaryId == project.DictionaryId && term.LanguageCode == language &&
                    words.Contains(term.Word) && contexts.Contains(term.Context))
                .ToListAsync(cancellationToken);
            targetMap = target.ToDictionary(term => (term.Word, term.Context));
        }

        var matches = matchingTerms.Select(term =>
        {
            targetMap.TryGetValue((term.Word, term.Context), out var translated);
            return term.GetDTO(translated);
        }).Concat(matchingLanguageTerms.Select(term => new DictionaryTermDTO
        {
            DictionaryId = term.DictionaryId,
            Word = term.SourceWord,
            SourceWord = term.SourceWord,
            Context = term.Context,
            TargetText = term.Word,
            SourceExplanation = term.Explanation,
            State = TranslationState.Translated,
            SourceLanguageCode = term.LanguageCode,
            IsLanguageSpecific = true,
            LastEditedByUserId = term.LastEditedByUserId,
        }));

        return matches.OrderBy(term => term.Word).ThenBy(term => term.Context)
            .ThenBy(term => term.SourceLanguageCode).ToArray();
    }

    [HttpPost("terms")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
    public async Task<IActionResult> AddTerm([FromBody] CreateDictionaryTermRequest request)
    {
        var actor = HttpContext.AuthenticatedUserOrThrow();

        if (!TranslationPermissions.CanEditSourceDictionary(actor.AccessCachedGroupsOrThrow()))
            return this.WorkingForbid("You don't have permission to edit the source dictionary");

        var word = request.Word.Trim();
        var context = request.Context.Trim();
        if (word.Length == 0)
            return BadRequest("Dictionary word cannot be empty");

        if (!await database.TranslationDictionaries.AnyAsync(d => d.Id == request.DictionaryId))
        {
            return NotFound();
        }

        if (await database.DictionaryTerms.AnyAsync(t => t.DictionaryId == request.DictionaryId &&
                t.Word == word && t.Context == context))
        {
            return Conflict("Dictionary term already exists for this context");
        }

        var term = new DictionaryTerm
        {
            DictionaryId = request.DictionaryId, Word = word, Context = context,
            Explanation = request.Explanation, LastEditedByUserId = actor.Id,
        };
        await database.DictionaryTerms.AddAsync(term);

        var languages = await database.TranslationProjects.Where(p => p.DictionaryId == request.DictionaryId)
            .Join(database.TranslationProjectLanguages, p => p.Id, l => l.ProjectId,
                (p, l) => new { l.LanguageCode, p.SourceLanguage })
            .Where(row => row.LanguageCode != row.SourceLanguage)
            .Select(row => row.LanguageCode).Distinct().ToListAsync();
        foreach (var language in languages)
        {
            await database.DictionaryTermTranslations.AddAsync(new DictionaryTermTranslation
            {
                DictionaryId = request.DictionaryId, Word = word, Context = context,
                LanguageCode = language, State = TranslationState.NeedsTranslation,
            });
        }

        await database.SaveChangesAsync();
        logger.LogInformation("Source dictionary term {Word} ({Context}) added to dictionary {DictionaryId} by " +
            "{Email}", word, context, request.DictionaryId, actor.Email);
        return CreatedAtAction(nameof(Terms), new { id = request.DictionaryId, language = "en" });
    }

    [HttpPut("terms/source")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
    public async Task<IActionResult> SaveSourceTerm([FromBody] SaveDictionaryTermRequest request)
    {
        var actor = HttpContext.AuthenticatedUserOrThrow();

        if (!TranslationPermissions.CanEditSourceDictionary(actor.AccessCachedGroupsOrThrow()))
            return this.WorkingForbid("You don't have permission to edit the source dictionary");

        var word = request.NewWord.Trim();
        var context = request.NewContext.Trim();
        if (word.Length == 0)
            return BadRequest("Dictionary word cannot be empty");

        var term = await database.DictionaryTerms.FindAsync(request.DictionaryId, request.Word, request.Context);
        if (term == null)
            return NotFound();

        var changed = term.Word != word || term.Context != context || term.Explanation != request.Explanation;
        if (!changed)
            return NoContent();

        if ((term.Word != word || term.Context != context) &&
            await database.DictionaryTerms.AnyAsync(t => t.DictionaryId == request.DictionaryId &&
                t.Word == word && t.Context == context))
        {
            return Conflict("Dictionary term already exists for this context");
        }

        var translations = await database.DictionaryTermTranslations.Where(t =>
                t.DictionaryId == request.DictionaryId && t.Word == request.Word && t.Context == request.Context)
            .ToListAsync();

        if (term.Word != word || term.Context != context)
        {
            // The term's word and context are part of its key, so its translation rows must be moved to the new
            // key. Keep their existing text and explanations so translators can use them as a reference.
            await using var transaction = await database.Database.BeginTransactionAsync();
            database.DictionaryTermTranslations.RemoveRange(translations);
            database.DictionaryTerms.Remove(term);
            await database.SaveChangesAsync();

            var updatedTerm = new DictionaryTerm
            {
                DictionaryId = request.DictionaryId,
                Word = word,
                Context = context,
                Explanation = request.Explanation,
                LastEditedByUserId = actor.Id,
            };
            updatedTerm.BumpUpdatedAt();
            await database.DictionaryTerms.AddAsync(updatedTerm);
            var updatedTranslations = new List<DictionaryTermTranslation>(translations.Count);
            foreach (var translation in translations)
            {
                var updatedTranslation = new DictionaryTermTranslation
                {
                    DictionaryId = request.DictionaryId,
                    Word = word,
                    Context = context,
                    LanguageCode = translation.LanguageCode,
                    Text = translation.Text,
                    Explanation = translation.Explanation,
                    State = TranslationState.NeedsTranslation,
                    LastEditedByUserId = translation.LastEditedByUserId,
                    Version = Guid.NewGuid(),
                };
                updatedTranslations.Add(updatedTranslation);
            }

            await database.DictionaryTermTranslations.AddRangeAsync(updatedTranslations);
            AddSourceTermUpdateNotifications(updatedTranslations, actor.Id, request.DictionaryId, request.Word,
                request.Context, word, context);
            await database.SaveChangesAsync();
            await transaction.CommitAsync();
            logger.LogInformation("Source dictionary term {OldWord} ({OldContext}) renamed to {Word} ({Context}) " +
                "in dictionary {DictionaryId} by {Email}", request.Word, request.Context, word, context,
                request.DictionaryId, actor.Email);
            return NoContent();
        }

        term.Explanation = request.Explanation;
        term.LastEditedByUserId = actor.Id;
        term.BumpUpdatedAt();
        foreach (var translation in translations)
        {
            translation.State = TranslationState.NeedsTranslation;
            translation.Version = Guid.NewGuid();
        }

        AddSourceTermUpdateNotifications(translations, actor.Id, request.DictionaryId, request.Word, request.Context,
            word, context);
        await database.SaveChangesAsync();
        logger.LogInformation("Source dictionary term {Word} ({Context}) updated in dictionary {DictionaryId} by " +
            "{Email}", word, context, request.DictionaryId, actor.Email);
        return NoContent();
    }

    [HttpPost("terms/language")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
    public async Task<IActionResult> AddLanguageTerm([FromBody] CreateDictionaryLanguageTermRequest request)
    {
        var actor = HttpContext.AuthenticatedUserOrThrow();

        if (!TranslationPermissions.CanEditTargetDictionary(actor.AccessCachedGroupsOrThrow()))
            return this.WorkingForbid("You don't have permission to edit the target dictionary");
        if (request.LanguageCode == "en")
            return BadRequest("Language-specific terms must use a non-English language");

        var word = request.Word.Trim();
        var sourceWord = request.SourceWord.Trim();
        var context = request.Context.Trim();
        if (word.Length == 0)
            return BadRequest("Dictionary word cannot be empty");
        if (sourceWord.Length == 0)
            return BadRequest("Source language word cannot be empty");

        if (!await database.TranslationDictionaries.AnyAsync(d => d.Id == request.DictionaryId))
        {
            return NotFound();
        }

        var languageIsConfigured = await database.TranslationProjects.Where(p => p.DictionaryId == request.DictionaryId)
            .Join(database.TranslationProjectLanguages, p => p.Id, l => l.ProjectId,
                (p, l) => l.LanguageCode).AnyAsync(code => code == request.LanguageCode);
        if (!languageIsConfigured)
            return BadRequest("Language is not configured for this dictionary");

        if (await database.DictionaryLanguageTerms.AnyAsync(t => t.DictionaryId == request.DictionaryId &&
                t.LanguageCode == request.LanguageCode && t.Word == word && t.Context == context))
        {
            return Conflict("Dictionary term already exists for this language and context");
        }

        await database.DictionaryLanguageTerms.AddAsync(new DictionaryLanguageTerm
        {
            DictionaryId = request.DictionaryId,
            LanguageCode = request.LanguageCode,
            Word = word,
            SourceWord = sourceWord,
            Context = context,
            Explanation = request.Explanation,
            LastEditedByUserId = actor.Id,
        });
        await database.SaveChangesAsync();
        logger.LogInformation("Language-specific dictionary term {Word} ({Context}) added for {LanguageCode} " +
            "in dictionary {DictionaryId} by {Email}", word, context, request.LanguageCode, request.DictionaryId,
            actor.Email);
        return CreatedAtAction(nameof(Terms), new { id = request.DictionaryId, language = request.LanguageCode });
    }

    [HttpPut("terms/language")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
    public async Task<IActionResult> SaveLanguageTerm([FromBody] SaveDictionaryLanguageTermRequest request)
    {
        var actor = HttpContext.AuthenticatedUserOrThrow();

        if (!TranslationPermissions.CanEditTargetDictionary(actor.AccessCachedGroupsOrThrow()))
            return this.WorkingForbid("You don't have permission to edit the target dictionary");
        if (request.LanguageCode == "en")
            return BadRequest("Language-specific terms must use a non-English language");

        var word = request.NewWord.Trim();
        var sourceWord = request.NewSourceWord.Trim();
        var context = request.NewContext.Trim();
        if (word.Length == 0)
            return BadRequest("Dictionary word cannot be empty");
        if (sourceWord.Length == 0)
            return BadRequest("Source language word cannot be empty");

        var term = await database.DictionaryLanguageTerms.FindAsync(request.DictionaryId, request.LanguageCode,
            request.Word, request.Context);
        if (term == null)
            return NotFound();

        var changed = term.Word != word || term.SourceWord != sourceWord || term.Context != context ||
            term.Explanation != request.Explanation;
        if (!changed)
            return NoContent();

        if ((term.Word != word || term.Context != context) &&
            await database.DictionaryLanguageTerms.AnyAsync(t => t.DictionaryId == request.DictionaryId &&
                t.LanguageCode == request.LanguageCode && t.Word == word && t.Context == context))
        {
            return Conflict("Dictionary term already exists for this language and context");
        }

        if (term.Word != word || term.Context != context)
        {
            await using var transaction = await database.Database.BeginTransactionAsync();
            database.DictionaryLanguageTerms.Remove(term);
            await database.SaveChangesAsync();
            var updatedTerm = new DictionaryLanguageTerm
            {
                DictionaryId = request.DictionaryId,
                LanguageCode = request.LanguageCode,
                Word = word,
                SourceWord = sourceWord,
                Context = context,
                Explanation = request.Explanation,
                LastEditedByUserId = actor.Id,
            };
            updatedTerm.BumpUpdatedAt();
            await database.DictionaryLanguageTerms.AddAsync(updatedTerm);
            await database.SaveChangesAsync();
            await transaction.CommitAsync();
            logger.LogInformation("Language-specific dictionary term {OldWord} ({OldContext}) renamed to " +
                "{Word} ({Context}) for {LanguageCode} in dictionary {DictionaryId} by {Email}", request.Word,
                request.Context, word, context, request.LanguageCode, request.DictionaryId, actor.Email);
            return NoContent();
        }

        term.Explanation = request.Explanation;
        term.SourceWord = sourceWord;
        term.LastEditedByUserId = actor.Id;
        term.BumpUpdatedAt();
        await database.SaveChangesAsync();
        logger.LogInformation("Language-specific dictionary term {Word} ({Context}) updated for {LanguageCode} " +
            "in dictionary {DictionaryId} by {Email}", word, context, request.LanguageCode, request.DictionaryId,
            actor.Email);
        return NoContent();
    }

    [HttpPut("terms/translation")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
    public async Task<IActionResult> SaveTranslation([FromBody] SaveDictionaryTranslationRequest request)
    {
        var actor = HttpContext.AuthenticatedUserOrThrow();

        if (!TranslationPermissions.CanEditTargetDictionary(actor.AccessCachedGroupsOrThrow()))
            return this.WorkingForbid("You don't have permission to translate in this dictionary");
        if (request.LanguageCode == "en")
            return Forbid();

        var state = request.State;
        if (!Enum.IsDefined(state))
            return BadRequest("Invalid state");
        if (state == TranslationState.Verified && !TranslationPermissions.CanVerify(actor.AccessCachedGroupsOrThrow()))
            return this.WorkingForbid("You don't have permission to verify translations in this dictionary");
        if (state != TranslationState.NeedsTranslation && string.IsNullOrWhiteSpace(request.Text))
            return BadRequest("Empty dictionary translations need translation");

        var value = await database.DictionaryTermTranslations.FindAsync(request.DictionaryId, request.Word,
            request.Context, request.LanguageCode);
        if (value == null)
            return NotFound();

        var previousEditor = value.LastEditedByUserId;
        var changed = value.Text != request.Text;
        value.Text = request.Text;
        value.Explanation = request.Explanation;
        value.State = state;
        value.Version = Guid.NewGuid();
        if (changed)
            value.LastEditedByUserId = actor.Id;

        if (changed && previousEditor != null && previousEditor != actor.Id)
        {
            await database.UserNotifications.AddAsync(new UserNotification
            {
                UserId = previousEditor.Value,
                DeduplicationKey = $"dictionary:{request.DictionaryId}:{request.Word}:{request.LanguageCode}:" +
                    value.Version,
                Type = UserNotificationType.DictionaryTermEdited,
                Message = $"Someone edited your {request.LanguageCode} dictionary entry for {request.Word}.",
                TargetPath = $"/translations/dictionaries/{request.DictionaryId}?language=" +
                    Uri.EscapeDataString(request.LanguageCode),
            });
        }

        await database.SaveChangesAsync();
        logger.LogInformation("Dictionary translation for {Word} ({Context}), language {LanguageCode}, " +
            "saved in dictionary {DictionaryId} by {Email}; text changed: {TextChanged}, state: {State}",
            request.Word, request.Context, request.LanguageCode, request.DictionaryId, actor.Email, changed, state);
        return NoContent();
    }

    [HttpPost("requests")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
    public async Task<IActionResult> RequestTerm([FromBody] CreateDictionaryTermRequest request)
    {
        var actor = HttpContext.AuthenticatedUserOrThrow();

        var word = request.Word.Trim();
        var context = request.Context.Trim();
        if (word.Length == 0)
            return BadRequest("Dictionary word cannot be empty");

        if (!await database.TranslationDictionaries.AnyAsync(d => d.Id == request.DictionaryId))
        {
            return NotFound();
        }

        if (await database.DictionaryTerms.AnyAsync(t => t.DictionaryId == request.DictionaryId &&
                t.Word == word && t.Context == context))
        {
            return Conflict("The dictionary term already exists for this context");
        }

        if (await database.DictionaryTermRequests.AnyAsync(r => r.DictionaryId == request.DictionaryId &&
                r.Word == word && r.Context == context && r.Approved == null))
        {
            return Conflict("A request for this word and context is already pending");
        }

        var termRequest = new DictionaryTermRequest
        {
            DictionaryId = request.DictionaryId, Word = word, Context = context,
            Explanation = request.Explanation, RequesterUserId = actor.Id,
        };
        await database.DictionaryTermRequests.AddAsync(termRequest);
        await database.SaveChangesAsync();
        logger.LogInformation("Dictionary term request {RequestId} for {Word} ({Context}) submitted by {Email}",
            termRequest.Id, word, context, actor.Email);
        return Accepted();
    }

    [HttpGet("requests")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.DictionaryManager, AllowAdmin = true)]
    public async Task<ActionResult<DictionaryTermRequestDTO[]>> PendingRequests()
    {
        return await database.DictionaryTermRequests.AsNoTracking().Where(r => r.Approved == null)
            .OrderBy(r => r.CreatedAt).Take(200)
            .Select(r => new DictionaryTermRequestDTO
            {
                Id = r.Id,
                DictionaryId = r.DictionaryId,
                Word = r.Word,
                Context = r.Context,
                Explanation = r.Explanation,
                RequesterUserId = r.RequesterUserId,
            }).ToArrayAsync();
    }

    [HttpPost("requests/{id:long}/approve")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.DictionaryManager, AllowAdmin = true)]
    public async Task<IActionResult> ApproveRequest(long id)
    {
        var actor = HttpContext.AuthenticatedUserOrThrow();
        var request = await database.DictionaryTermRequests.FindAsync(id);
        if (request == null)
            return NotFound();

        if (request.Approved != null)
            return Conflict("Request already approved");

        if (await database.DictionaryTerms.AnyAsync(t => t.DictionaryId == request.DictionaryId &&
                t.Word == request.Word && t.Context == request.Context))
        {
            return Conflict("Term already exists");
        }

        await database.DictionaryTerms.AddAsync(new DictionaryTerm
        {
            DictionaryId = request.DictionaryId, Word = request.Word, Context = request.Context,
            Explanation = request.Explanation, LastEditedByUserId = actor.Id,
        });

        var languages = await database.TranslationProjects.Where(p => p.DictionaryId == request.DictionaryId)
            .Join(database.TranslationProjectLanguages, p => p.Id, l => l.ProjectId,
                (p, l) => new { p.SourceLanguage, l.LanguageCode })
            .Where(pair => pair.LanguageCode != pair.SourceLanguage)
            .Select(pair => pair.LanguageCode).Distinct().ToListAsync();
        foreach (var language in languages)
        {
            await database.DictionaryTermTranslations.AddAsync(new DictionaryTermTranslation
            {
                DictionaryId = request.DictionaryId, Word = request.Word, Context = request.Context,
                LanguageCode = language, State = TranslationState.NeedsTranslation,
            });
        }

        request.Approved = true;
        request.DecidedByUserId = actor.Id;
        request.DecidedAt = DateTime.UtcNow;
        await database.UserNotifications.AddAsync(new UserNotification
        {
            UserId = request.RequesterUserId,
            DeduplicationKey = $"dictionary-request:{id}:approved",
            Type = UserNotificationType.DictionaryRequestApproved,
            Message = $"Your dictionary request for {request.Word} was approved.",
            TargetPath = $"/translations/dictionaries/{request.DictionaryId}",
        });
        await database.ActionLogEntries.AddAsync(new ActionLogEntry(
            $"Dictionary term request {id} for {request.Word} ({request.Context}) approved")
        {
            PerformedById = actor.Id,
        });

        await database.SaveChangesAsync();
        logger.LogInformation("Dictionary term request {RequestId} for {Word} ({Context}) approved by {Email}",
            id, request.Word, request.Context, actor.Email);
        return NoContent();
    }

    [HttpPost("requests/{id:long}/reject")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.DictionaryManager, AllowAdmin = true)]
    public async Task<IActionResult> RejectRequest(long id)
    {
        var actor = HttpContext.AuthenticatedUserOrThrow();
        var request = await database.DictionaryTermRequests.FindAsync(id);
        if (request == null || request.Approved != null)
            return NotFound();

        request.Approved = false;
        request.DecidedByUserId = actor.Id;
        request.DecidedAt = DateTime.UtcNow;
        await database.UserNotifications.AddAsync(new UserNotification
        {
            UserId = request.RequesterUserId,
            DeduplicationKey = $"dictionary-request:{id}:rejected",
            Type = UserNotificationType.DictionaryRequestRejected,
            Message = $"Your dictionary request for {request.Word} was declined.",
            TargetPath = $"/translations/dictionaries/{request.DictionaryId}",
        });
        await database.ActionLogEntries.AddAsync(new ActionLogEntry(
            $"Dictionary term request {id} for {request.Word} ({request.Context}) rejected")
        {
            PerformedById = actor.Id,
        });
        await database.SaveChangesAsync();
        logger.LogInformation("Dictionary term request {RequestId} for {Word} ({Context}) rejected by {Email}",
            id, request.Word, request.Context, actor.Email);
        return NoContent();
    }

    private static bool DictionaryWordMatches(string sourceText, string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        return Regex.IsMatch(sourceText, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(word)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private void AddSourceTermUpdateNotifications(IEnumerable<DictionaryTermTranslation> translations, long actorId,
        long dictionaryId, string oldWord, string oldContext, string newWord, string newContext)
    {
        foreach (var translation in translations)
        {
            if (translation.LastEditedByUserId is not { } previousEditor || previousEditor == actorId)
                continue;

            var oldTerm = string.IsNullOrEmpty(oldContext) ? oldWord : $"{oldWord} ({oldContext})";
            var newTerm = string.IsNullOrEmpty(newContext) ? newWord : $"{newWord} ({newContext})";
            var updateDescription = oldWord == newWord && oldContext == newContext ?
                $"The source dictionary entry for {newTerm} was updated." :
                $"The source dictionary entry for {oldTerm} was updated to {newTerm}.";
            database.UserNotifications.Add(new UserNotification
            {
                UserId = previousEditor,
                DeduplicationKey = $"dictionary-source-update:{dictionaryId}:{translation.Version}",
                Type = UserNotificationType.DictionaryTermEdited,
                Message = $"{updateDescription} Your {translation.LanguageCode} translation may need review.",
                TargetPath = $"/translations/dictionaries/{dictionaryId}?language=" +
                    Uri.EscapeDataString(translation.LanguageCode),
            });
        }
    }
}
