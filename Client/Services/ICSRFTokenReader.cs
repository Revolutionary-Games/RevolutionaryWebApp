namespace RevolutionaryWebApp.Client.Services;

using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using Microsoft.JSInterop;
using Models;
using RevolutionaryWebApp.Shared;
using RevolutionaryWebApp.Shared.Models;

/// <summary>
///   Reads the CSRF token on the current page and makes it available
/// </summary>
public interface ICSRFTokenReader
{
    public bool Valid { get; }
    public int TimeRemaining { get; }
    public string Token { get; }
    public long? InitialUserId { get; }
}

public class CSRFTokenReader : ICSRFTokenReader
{
    private readonly IJSRuntime jsRuntime;
    private readonly CurrentUserInfo currentUserInfo;
    private readonly ClientMediaLinkConverter mediaLinkConverter;

    private UserToken tokenAndUser = null!;

    private DateTime csrfTokenExpires;

    public CSRFTokenReader(IJSRuntime jsRuntime, CurrentUserInfo currentUserInfo,
        ClientMediaLinkConverter mediaLinkConverter)
    {
        this.jsRuntime = jsRuntime;
        this.currentUserInfo = currentUserInfo;
        this.mediaLinkConverter = mediaLinkConverter;
    }

    public bool Valid => TimeRemaining > 0 && !string.IsNullOrEmpty(Token);

    public int TimeRemaining => (int)(csrfTokenExpires - DateTime.UtcNow).TotalSeconds;

    public string Token => tokenAndUser.CSRF;

    public long? InitialUserId => tokenAndUser.User?.Id;

    public async Task Read()
    {
        var rawData = await jsRuntime.InvokeAsync<string>("getCSRFToken");

        tokenAndUser = JsonSerializer.Deserialize<UserToken>(rawData) ??
            throw new InvalidOperationException("The page we loaded from didn't contain CSRF token");

        var timeStr = await jsRuntime.InvokeAsync<string>("getCSRFTokenExpiry");
        var configRead = jsRuntime.InvokeAsync<string>("getSiteConfig");

        csrfTokenExpires = DateTime.Parse(timeStr, null, DateTimeStyles.RoundtripKind);

        // Send our initial user info through
        currentUserInfo.OnReceivedOurInfo(tokenAndUser.User);

        // And other initial page data
        rawData = await configRead;

        try
        {
            var config = JsonSerializer.Deserialize<SiteConfigData>(rawData) ??
                throw new InvalidOperationException("The page we loaded from didn't contain site config info");

            mediaLinkConverter.OnReceiveBaseUrl(config.MediaBaseUrl);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to load site config data: {e.Message}");
        }
    }

    public async Task ReportInitialUserIdToLocalStorage(ILocalStorageService localStorage)
    {
        try
        {
            await localStorage.SetItemAsync(AppInfo.LocalStorageUserInfo, new LocalStorageUserInfo
            {
                LastSignedInUserId = InitialUserId,
            });
        }
        catch (Exception e)
        {
            await Console.Error.WriteLineAsync(
                $"Cannot set item in local storage, detecting login actions from other windows won't work: {e}");
        }
    }
}
