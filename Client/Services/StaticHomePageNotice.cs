namespace ThriveDevCenter.Client.Services
{
    using System.Threading.Tasks;
    using Microsoft.JSInterop;

    public class StaticHomePageNotice
    {
        private readonly IJSRuntime jsRuntime;
        private bool fetched;
        private string? value;

        public StaticHomePageNotice(IJSRuntime jsRuntime)
        {
            this.jsRuntime = jsRuntime;
        }

        public async Task<string?> ReadNotice()
        {
            if (fetched)
                return value;

            value = await jsRuntime.InvokeAsync<string>("getStaticHomePageNotice");
            fetched = true;
            return value;
        }
    }
}
