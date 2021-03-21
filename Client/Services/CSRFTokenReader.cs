namespace ThriveDevCenter.Client.Services
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Blazored.LocalStorage;
    using Microsoft.JSInterop;
    using Models;
    using ThriveDevCenter.Shared;
    using ThriveDevCenter.Shared.Models;

    /// <summary>
    ///   Reads the CSRF token on the current page and makes it available
    /// </summary>
    public class CSRFTokenReader
    {
        private readonly IJSRuntime jsRuntime;
        private readonly CurrentUserInfo currentUserInfo;

        private UserToken tokenAndUser;

        private DateTime csrfTokenExpires;

        public CSRFTokenReader(IJSRuntime jsRuntime, CurrentUserInfo currentUserInfo)
        {
            this.jsRuntime = jsRuntime;
            this.currentUserInfo = currentUserInfo;
        }

        public bool Valid => TimeRemaining > 0 && !string.IsNullOrEmpty(Token);

        public int TimeRemaining => (int)(csrfTokenExpires - DateTime.UtcNow).TotalSeconds;

        public string Token => tokenAndUser?.CSRF;

        public long? InitialUserId => tokenAndUser.User?.Id;

        public async Task Read()
        {
            var rawData = await jsRuntime.InvokeAsync<string>("getCSRFToken");

            tokenAndUser = JsonSerializer.Deserialize<UserToken>(rawData);

            if (tokenAndUser == null)
                throw new InvalidOperationException("The page we loaded from didn't contain CSRF token");

            var timeStr = await jsRuntime.InvokeAsync<string>("getCSRFTokenExpiry");

            csrfTokenExpires = DateTime.Parse(timeStr, null, DateTimeStyles.RoundtripKind);

            // Send our initial user info through
            currentUserInfo.OnReceivedOurInfo(tokenAndUser.User);
        }

        public async Task ReportInitialUserIdToLocalStorage(ILocalStorageService localStorage)
        {
            try
            {
                await localStorage.SetItemAsync(AppInfo.LocalStorageUserInfo, new LocalStorageUserInfo()
                {
                    LastSignedInUserId = InitialUserId
                });
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync(
                    $"Cannot set item in local storage, detecting login actions from other windows won't work: {e}");
            }
        }
    }
}
