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
    ///   Client requests the server to send a list of available jobs. Reply is either <see cref="JobsList"/> or
    ///   <see cref="ActiveJobDetails"/> if the client is supposed to be running a job.
    /// </summary>
    GetAvailableJobs,

    /// <summary>
    ///   Server sends a list of available jobs that the client can take. The output is JSON-encoded AvailableJobsList.
    /// </summary>
    JobsList,

    /// <summary>
    ///   Client requests the server to start a job. The server either accepts or sends an <see cref="Error"/>.
    ///   The output field needs to be ":"-separated 3 longs which identify the job to start.
    /// </summary>
    RequestStartJob,

    /// <summary>
    ///   Server sends the details of the job that was started. Note that if a client loses connection to the server
    ///   and has an active job, the server will send this message when reconnecting and trying to get available jobs.
    ///   If the client cannot continue the job, it needs to send a <see cref="FinalStatus"/> with the failure.
    ///   The output is JSON-encoded RunningJobDetails.
    /// </summary>
    ActiveJobDetails,
}
