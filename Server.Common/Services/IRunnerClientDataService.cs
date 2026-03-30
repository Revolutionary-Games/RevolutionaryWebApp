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
}

/// <summary>
///   Just a plain object that holds the data for <see cref="IRunnerClientDataService"/>
/// </summary>
public class RunnerClientDataServiceObjet : IRunnerClientDataService
{
    public string ConnectionKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    public string ServerUrl { get; set; } = "https://dev.revolutionarygamesstudio.com/runnerConnection";
}
