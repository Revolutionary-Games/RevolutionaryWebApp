namespace RevolutionaryWebApp.Server.Services;

using System.Linq;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Translations;
using Shared.Models;

/// <summary>
///   Builds the ordered database query used by the recent translation activity views.
/// </summary>
public static class TranslationActivityQuery
{
    public static IOrderedQueryable<TranslationActivityDTO> Create(ApplicationDbContext database,
        TranslationProject project, string language, TranslationActivityView view)
    {
        TranslationActivityFilter.TryParse(null, view, out var selected);
        return Create(database, project, language, selected);
    }

    public static IOrderedQueryable<TranslationActivityDTO> Create(ApplicationDbContext database,
        TranslationProject project, string? language, TranslationActivityView[] selected, long? userId = null)
    {
        // All projections use the same fields, so the combined view can be paged as a SQL UNION ALL.
        var comments = database.TranslationComments.AsNoTracking().Where(comment => comment.ProjectId == project.Id &&
                !comment.Deleted &&
                (language == null || comment.LanguageCode == language ||
                    comment.LanguageCode == project.SourceLanguage))
            .Select(comment => new TranslationActivityDTO
            {
                Id = comment.Id, Key = comment.Key, LanguageCode = comment.LanguageCode,
                UserId = comment.AuthorUserId, CreatedAt = comment.CreatedAt,
                Description = "Comment", Text = comment.RawMarkdown, PreviousText = null,
                State = null, PreviousState = null,
            });
        var suggestions = database.TranslationSuggestions.AsNoTracking()
            .Where(suggestion => suggestion.ProjectId == project.Id &&
                (language == null || suggestion.LanguageCode == language))
            .Select(suggestion => new TranslationActivityDTO
            {
                Id = suggestion.Id, Key = suggestion.Key, LanguageCode = suggestion.LanguageCode,
                UserId = suggestion.AuthorUserId, CreatedAt = suggestion.CreatedAt,
                Description = "Suggestion", Text = suggestion.Text, PreviousText = null,
                State = null, PreviousState = null,
            });
        var keys = database.TranslationUnits.AsNoTracking()
            .Where(unit => unit.ProjectId == project.Id && unit.Active)
            .Select(unit => new TranslationActivityDTO
            {
                Id = 0L, Key = unit.Key, LanguageCode = project.SourceLanguage,
                UserId = null, CreatedAt = unit.CreatedAt,
                Description = "New key", Text = unit.SourceText, PreviousText = null,
                State = null, PreviousState = null,
            });
        var edits = database.TranslationChanges.AsNoTracking().Where(change => change.ProjectId == project.Id &&
                (language == null || change.LanguageCode == language ||
                    change.LanguageCode == project.SourceLanguage) &&
                (change.LanguageCode == project.SourceLanguage ?
                    selected.Contains(TranslationActivityView.Source) :
                    selected.Contains(TranslationActivityView.Edits)))
            .Select(change => new TranslationActivityDTO
            {
                Id = change.Id, Key = change.Key, LanguageCode = change.LanguageCode,
                UserId = change.ActorUserId, CreatedAt = change.CreatedAt,
                Description = "Translation edit", Text = change.NewText, PreviousText = change.OldText,
                State = change.NewState, PreviousState = change.OldState,
            });

        var activity = edits.Concat(comments.Where(_ => selected.Contains(TranslationActivityView.Comments)))
            .Concat(keys.Where(_ => selected.Contains(TranslationActivityView.Keys)))
            .Concat(suggestions.Where(_ => selected.Contains(TranslationActivityView.Suggestions)));
        if (userId != null)
            activity = activity.Where(item => item.UserId == userId);

        var ordered = activity.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id);

        return ordered.ThenBy(item => item.Key).ThenBy(item => item.Description).ThenBy(item => item.LanguageCode);
    }
}
