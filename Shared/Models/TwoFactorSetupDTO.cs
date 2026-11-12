namespace RevolutionaryWebApp.Shared.Models;

public class TwoFactorSetupDTO
{
    public string Secret { get; set; } = string.Empty;

    public string QrCodeDataUrl { get; set; } = string.Empty;
}
