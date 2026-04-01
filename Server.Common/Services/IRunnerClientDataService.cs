namespace RevolutionaryWebApp.Server.Common.Services;

public interface IRunnerClientDataService
{
    /// <summary>
    ///   Key name needed to connect to the backend
    /// </summary>
    public string ConnectionKey { get; }

    /// <summary>
    ///   Secret authentication key
    /// </summary>
    public string SecretKey { get; }

    public string ServerUrl { get; }

    public long MaxCacheSize { get; }

    /// <summary>
    ///   How much cache to keep on clean. 0 means to prune all.
    /// </summary>
    public long KeepCacheSize { get; }

    public float PruneCacheAfterSizeFraction { get; }
}

/// <summary>
///   Just a plain object that holds the data for <see cref="IRunnerClientDataService"/>
/// </summary>
public class RunnerClientDataServiceObjet : IRunnerClientDataService
{
    public string ConnectionKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    public string ServerUrl { get; set; } = "https://dev.revolutionarygamesstudio.com/runnerConnection";

    public long MaxCacheSize { get; set; }

    public long KeepCacheSize { get; set; }

    public float PruneCacheAfterSizeFraction { get; set; } = 0.5f;
}
