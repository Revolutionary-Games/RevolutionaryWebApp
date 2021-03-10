namespace ThriveDevCenter.Client.Shared
{
    using System;
    using System.Globalization;
    using System.Threading.Tasks;
    using Microsoft.JSInterop;

    /// <summary>
    ///   Reads the CSRF token on the current page and makes it available
    /// </summary>
    public class CSRFTokenReader
    {
        private readonly IJSRuntime jsRuntime;

        private string csrfToken;
        private DateTime csrfTokenExpires;

        public CSRFTokenReader(IJSRuntime jsRuntime)
        {
            this.jsRuntime = jsRuntime;
        }

        public bool Valid => !string.IsNullOrEmpty(csrfToken) && TimeRemaining > 0;

        public int TimeRemaining => (int)(csrfTokenExpires - DateTime.UtcNow).TotalSeconds;

        public async Task Read()
        {
            csrfToken = await jsRuntime.InvokeAsync<string>("getCSRFToken");

            var timeStr = await jsRuntime.InvokeAsync<string>("getCSRFTokenExpiry");

            csrfTokenExpires = DateTime.Parse(timeStr, null, DateTimeStyles.RoundtripKind);
        }
    }
}
