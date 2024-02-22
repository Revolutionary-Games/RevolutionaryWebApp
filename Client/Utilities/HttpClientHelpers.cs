namespace RevolutionaryWebApp.Client.Utilities;

using System.Text.Json;

public static class HttpClientHelpers
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>
    ///   As there's no way to globally configure this, this returns options with serializers to match the
    ///   serializers the server configures
    /// </summary>
    /// <returns>The configured options</returns>
    /// <remarks>
    ///   <para>
    ///     TODO: now that .net6 is here and there's a default TimeSpan converter this is not really needed anymore
    ///   </para>
    /// </remarks>
    public static JsonSerializerOptions GetOptionsWithSerializers()
    {
        return Options;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var result = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        return result;
    }
}
