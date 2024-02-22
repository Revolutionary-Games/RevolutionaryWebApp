namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Microsoft.Extensions.Logging;
using Models;
using Renci.SshNet.Common;
using Services;
using Shared.Models;

public abstract class BaseProvisionServerJob
{
    // TODO: put this somewhere more sensible
    public const string GeneralProvisionCommandPart =
        "sudo dnf install -y --refresh podman curl git git-lfs gawk dotnet-runtime-6.0 && " +
        "git lfs install && sudo mkdir -p /executor_cache";

    protected readonly ILogger<BaseProvisionServerJob> Logger;
    protected readonly NotificationsEnabledDb Database;

    protected BaseProvisionServerJob(ILogger<BaseProvisionServerJob> logger,
        NotificationsEnabledDb database)
    {
        Logger = logger;
        Database = database;
    }

    protected bool CheckServerDataIsFineForProvisioning(long id, BaseServer? server)
    {
        if (server == null)
        {
            Logger.LogWarning("Server {Id} not found for provisioning", id);
            return true;
        }

        if (server.ProvisionedFully)
            return true;

        if (server.Status != ServerStatus.Provisioning)
            throw new Exception("Server status is not Provisioning for the provisioning job");

        return false;
    }

    // TODO: make this async
    protected async Task PerformProvisioningCommands(BaseServer server, string command)
    {
        Logger.LogInformation("Beginning SSH connect to provision server at: {PublicAddress}",
            server.PublicAddress);

        // TODO: there should probably be a maximum number of times this is attempted
        IBaseSSHAccess sshAccess;
        try
        {
            sshAccess = ConnectWithSSH(server);
        }
        catch (SocketException)
        {
            Logger.LogWarning("Connection failed (socket exception), server is probably not up yet");
            return;
        }
        catch (SshOperationTimeoutException)
        {
            Logger.LogWarning("Connection failed (ssh timed out), server is probably not up yet");
            return;
        }

        Logger.LogInformation("Connected, running provisioning command...");

        var start = DateTime.UtcNow;
        var result = sshAccess.RunCommand(command);

        if (!result.Success)
        {
            Logger.LogWarning("Failed provision result ({ExitCode}: {Result}", result.ExitCode, result.Result);
            throw new Exception($"Provisioning commands failed ({result.ExitCode}): {result.Error}");
        }

        // Now fully provisioned
        server.ProvisionedFully = true;
        server.Status = ServerStatus.Running;
        server.LastMaintenance = DateTime.UtcNow;
        server.WantsMaintenance = false;
        server.StatusLastChecked = DateTime.UtcNow;
        server.ReservationType = ServerReservationType.None;
        server.BumpUpdatedAt();
        await Database.SaveChangesAsync();

        var elapsed = DateTime.UtcNow - start;
        Logger.LogInformation("Completed provisioning for server {Id}, elapsed: {Elapsed}", server.Id, elapsed);
    }

    protected abstract IBaseSSHAccess ConnectWithSSH(BaseServer server);
}
