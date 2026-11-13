namespace RevolutionaryWebApp.Shared.Models;

public class SudoModeRequiredDTO
{
    public bool TwoFactorEnabled { get; set; }

    public string Message { get; set; } = string.Empty;
}
