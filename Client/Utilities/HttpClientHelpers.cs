namespace ThriveDevCenter.Client.Utilities
{
    using System.Text.Json;
    using ThriveDevCenter.Shared.Converters;

    public static class HttpClientHelpers
    {
        private static readonly JsonSerializerOptions Options = CreateOptions();

        /// <summary>
        ///   As there's no way to globally configure this, this returns options with serializers to match the
        ///   serializers the server configures
        /// </summary>
        /// <returns>The configured options</returns>
        public static JsonSerializerOptions GetOptionsWithSerializers()
        {
            return Options;
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var result = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            result.Converters.Add(new TimeSpanConverter());
            return result;
        }
    }
}
