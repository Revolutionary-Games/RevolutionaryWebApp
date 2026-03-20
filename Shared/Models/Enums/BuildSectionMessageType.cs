namespace RevolutionaryWebApp.Shared.Models.Enums;

public enum BuildSectionMessageType
{
    SectionStart,
    BuildOutput,
    SectionEnd,
    FinalStatus,
    Error,

    /// <summary>
    ///   Server demands authentication from the connecting client
    /// </summary>
    AuthDemand,

    /// <summary>
    ///   Client tells its secret key
    /// </summary>
    AuthResponse,

    /// <summary>
    ///   Server accepts the client. It can now use all normal commands as the handshake is done
    /// </summary>
    AuthSuccess,

    /// <summary>
    ///   The client must send a heartbeat to the server every minute to keep a connection alive
    /// </summary>
    HeartBeat,

    /// <summary>
    ///   Server notifies the client that there are new jobs (potentially) available
    /// </summary>
    NewJobsAvailable,
}
