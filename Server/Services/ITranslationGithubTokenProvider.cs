namespace RevolutionaryWebApp.Server.Services;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared.Models.Enums;

/// <summary>
///   Provides a GitHub token for publishing translations.
/// </summary>
public interface ITranslationGithubTokenProvider
{
    /// <summary>
    ///   A GitHub token for publishing translation pull requests (and some other actions).
    /// </summary>
    /// <returns>A valid token or null</returns>
    public Task<string?> GetToken(CancellationToken cancellationToken);
}

public sealed class TranslationGithubTokenProvider : ITranslationGithubTokenProvider
{
    private readonly ApplicationDbContext database;
    private readonly IDataProtector protector;

    public TranslationGithubTokenProvider(ApplicationDbContext database,
        IDataProtectionProvider dataProtectionProvider)
    {
        this.database = database;
        protector = dataProtectionProvider.CreateProtector(GithubToken.TranslationsProtectionPurpose);
    }

    public async Task<string?> GetToken(CancellationToken cancellationToken)
    {
        var ciphertext = await database.GithubTokens.AsNoTracking()
            .Where(token => token.Purpose == GithubTokenPurpose.Translations)
            .OrderByDescending(token => token.Id)
            .Select(token => token.Ciphertext)
            .FirstOrDefaultAsync(cancellationToken);
        return ciphertext == null ? null : protector.Unprotect(ciphertext);
    }
}
