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
        // All projections use the same fields so the combined view can be paged as a SQL UNION ALL.
        var comments = database.TranslationComments.AsNoTracking().Where(comment => comment.ProjectId == project.Id &&
                !comment.Deleted &&
                (comment.LanguageCode == language || comment.LanguageCode == project.SourceLanguage))
            .Select(comment => new TranslationActivityDTO
            {
                Id = comment.Id, Key = comment.Key, LanguageCode = comment.LanguageCode,
                UserId = (long?)comment.AuthorUserId, CreatedAt = comment.CreatedAt,
                Description = "Comment", Text = comment.RawMarkdown, PreviousText = (string?)null,
                State = (TranslationState?)null, PreviousState = (TranslationState?)null,
            });
        var keys = database.TranslationUnits.AsNoTracking()
            .Where(unit => unit.ProjectId == project.Id && unit.Active)
            .Select(unit => new TranslationActivityDTO
            {
                Id = 0L, Key = unit.Key, LanguageCode = language,
                UserId = (long?)null, CreatedAt = unit.CreatedAt,
                Description = "New key", Text = unit.SourceText, PreviousText = (string?)null,
                State = (TranslationState?)null, PreviousState = (TranslationState?)null,
            });
        var edits = database.TranslationChanges.AsNoTracking().Where(change => change.ProjectId == project.Id &&
                (view == TranslationActivityView.Source ? change.LanguageCode == project.SourceLanguage :
                    change.LanguageCode == language || change.LanguageCode == project.SourceLanguage))
            .Select(change => new TranslationActivityDTO
            {
                Id = change.Id, Key = change.Key, LanguageCode = change.LanguageCode,
                UserId = change.ActorUserId, CreatedAt = change.CreatedAt,
                Description = "Translation edit", Text = change.NewText, PreviousText = change.OldText,
                State = (TranslationState?)change.NewState, PreviousState = (TranslationState?)change.OldState,
            });

        var activity = view switch
        {
            TranslationActivityView.All => edits.Concat(comments).Concat(keys),
            TranslationActivityView.Comments => comments,
            TranslationActivityView.Keys => keys,
            _ => edits,
        };
        var ordered = activity.OrderByDescending(item => item.CreatedAt);

        // Translation units have a project/key primary key, so the new-key view has no numeric ID to sort by.
        if (view != TranslationActivityView.Keys)
            ordered = ordered.ThenByDescending(item => item.Id);

        return ordered.ThenBy(item => item.Key).ThenBy(item => item.Description).ThenBy(item => item.LanguageCode);
    }
}
