namespace RevolutionaryWebApp.Server.Models.Translations;

using Microsoft.EntityFrameworkCore;

public static class TranslationModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TranslationProject>(entity =>
        {
            entity.HasOne(p => p.Dictionary).WithMany(d => d.Projects).HasForeignKey(p => p.DictionaryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TranslationProjectLanguage>(entity =>
        {
            entity.HasKey(l => new { l.ProjectId, l.LanguageCode });
            entity.HasOne(l => l.Project).WithMany(p => p.Languages).HasForeignKey(l => l.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TranslationUnit>(entity =>
        {
            entity.HasKey(u => new { u.ProjectId, u.Key });
            entity.HasOne(u => u.Project).WithMany(p => p.Units).HasForeignKey(u => u.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TranslationValue>(entity =>
        {
            entity.HasKey(v => new { v.ProjectId, v.Key, v.LanguageCode });
            entity.HasOne(v => v.Unit).WithMany(u => u.Values).HasForeignKey(v => new { v.ProjectId, v.Key })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(v => v.Language).WithMany(l => l.Values)
                .HasForeignKey(v => new { v.ProjectId, v.LanguageCode }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(v => v.LastEditedByUser).WithMany(u => u.LastEditedTranslationValues)
                .HasForeignKey(v => v.LastEditedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TranslationChange>(entity =>
        {
            entity.HasOne(c => c.Value).WithMany(v => v.Changes)
                .HasForeignKey(c => new { c.ProjectId, c.Key, c.LanguageCode })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(c => c.ActorUser).WithMany(u => u.ActorTranslationChanges).HasForeignKey(c => c.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(c => c.ContributorUser).WithMany(u => u.ContributorTranslationChanges)
                .HasForeignKey(c => c.ContributorUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TranslationImportConflict>(entity =>
        {
            entity.HasOne(c => c.Value).WithMany(v => v.ImportConflicts)
                .HasForeignKey(c => new { c.ProjectId, c.Key, c.LanguageCode })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TranslationComment>(entity =>
        {
            entity.HasOne(c => c.Value).WithMany(v => v.Comments)
                .HasForeignKey(c => new { c.ProjectId, c.Key, c.LanguageCode })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(c => c.AuthorUser).WithMany(u => u.AuthoredTranslationComments)
                .HasForeignKey(c => c.AuthorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TranslationSuggestion>(entity =>
        {
            entity.HasOne(s => s.Value).WithMany(v => v.Suggestions)
                .HasForeignKey(s => new { s.ProjectId, s.Key, s.LanguageCode })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(s => s.AuthorUser).WithMany(u => u.AuthoredTranslationSuggestions)
                .HasForeignKey(s => s.AuthorUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(s => s.ResolvedByUser).WithMany(u => u.ResolvedTranslationSuggestions)
                .HasForeignKey(s => s.ResolvedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TranslationSuggestionVote>(entity =>
        {
            entity.HasKey(v => new { v.SuggestionId, v.UserId });
            entity.ToTable(t => t.HasCheckConstraint("translation_suggestion_vote_value_check", "value IN (-1, 1)"));
            entity.HasOne(v => v.Suggestion).WithMany(s => s.Votes).HasForeignKey(v => v.SuggestionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(v => v.User).WithMany(u => u.TranslationSuggestionVotes).HasForeignKey(v => v.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DictionaryTerm>(entity =>
        {
            entity.HasKey(t => new { t.DictionaryId, t.Word, t.Context });
            entity.HasOne(t => t.Dictionary).WithMany(d => d.Terms).HasForeignKey(t => t.DictionaryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(t => t.LastEditedByUser).WithMany(u => u.LastEditedDictionaryTerms)
                .HasForeignKey(t => t.LastEditedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DictionaryTermTranslation>(entity =>
        {
            entity.HasKey(t => new { t.DictionaryId, t.Word, t.Context, t.LanguageCode });
            entity.HasOne(t => t.Term).WithMany(d => d.Translations)
                .HasForeignKey(t => new { t.DictionaryId, t.Word, t.Context })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(t => t.LastEditedByUser).WithMany(u => u.LastEditedDictionaryTermTranslations)
                .HasForeignKey(t => t.LastEditedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DictionaryLanguageTerm>(entity =>
        {
            entity.HasKey(t => new { t.DictionaryId, t.LanguageCode, t.Word, t.Context });
            entity.HasOne(t => t.Dictionary).WithMany(d => d.LanguageTerms).HasForeignKey(t => t.DictionaryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(t => t.LastEditedByUser).WithMany().HasForeignKey(t => t.LastEditedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DictionaryTermRequest>(entity =>
        {
            entity.HasOne(r => r.Dictionary).WithMany(d => d.Requests).HasForeignKey(r => r.DictionaryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(r => r.RequesterUser).WithMany(u => u.RequestedDictionaryTermRequests)
                .HasForeignKey(r => r.RequesterUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(r => r.DecidedByUser).WithMany(u => u.DecidedDictionaryTermRequests)
                .HasForeignKey(r => r.DecidedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TranslationSubscription>(entity =>
        {
            entity.HasKey(s => new { s.UserId, s.ProjectId, s.LanguageCode, s.EventType });
            entity.HasOne(s => s.User).WithMany(u => u.TranslationSubscriptions)
                .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(s => s.ProjectLanguage).WithMany(l => l.Subscriptions)
                .HasForeignKey(s => new { s.ProjectId, s.LanguageCode }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TranslationSyncRun>(entity =>
        {
            entity.HasOne(r => r.Project).WithMany(p => p.SyncRuns).HasForeignKey(r => r.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TranslationPullRequest>(entity =>
        {
            entity.HasKey(p => p.ProjectId);
            entity.HasOne(p => p.Project).WithOne(project => project.PullRequest)
                .HasForeignKey<TranslationPullRequest>(p => p.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
