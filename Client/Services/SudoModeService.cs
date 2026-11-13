namespace RevolutionaryWebApp.Client.Services;

using System.Threading.Tasks;
using Blazored.Modal;
using Blazored.Modal.Services;
using RevolutionaryWebApp.Shared.Models;
using Shared.Modals;

/// <summary>
///   Shares one sudo prompt between concurrent requests.
/// </summary>
public class SudoModeService(IModalService modal)
{
    private Task<bool>? pendingPrompt;

    public int Generation { get; private set; }

    public async Task<bool> Request(SudoModeRequiredDTO challenge, int requestGeneration)
    {
        // A slow response may arrive after another request has already completed the challenge.
        if (requestGeneration != Generation)
            return true;

        var prompt = pendingPrompt ??= ShowPrompt(challenge);
        try
        {
            return await prompt;
        }
        finally
        {
            if (ReferenceEquals(pendingPrompt, prompt))
                pendingPrompt = null;
        }
    }

    private async Task<bool> ShowPrompt(SudoModeRequiredDTO challenge)
    {
        var parameters = new ModalParameters
        {
            { nameof(EnterSudoMode.TwoFactorEnabled), challenge.TwoFactorEnabled },
        };
        var prompt = modal.Show<EnterSudoMode>("Enter sudo mode", parameters, new ModalOptions
        {
            DisableBackgroundCancel = true,
        });
        var result = await prompt.Result;
        if (result.Cancelled || result.Data is not true)
            return false;

        ++Generation;
        return true;
    }
}
