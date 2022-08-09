namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared.Models;

public class ProvisionExternalServerJob : BaseProvisionServerJob
{
    private readonly IBackgroundJobClient jobClient;
    private readonly IExternalServerSSHAccess sshAccess;

    public ProvisionExternalServerJob(ILogger<ProvisionExternalServerJob> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient, IExternalServerSSHAccess sshAccess) : base(logger, database)
    {
        this.jobClient = jobClient;
        this.sshAccess = sshAccess;
    }

    public async Task Execute(long id, CancellationToken cancellationToken)
    {
        var server = await Database.ExternalServers.FindAsync(id);

        if (CheckServerDataIsFineForProvisioning(id, server) || server == null)
            return;

        await PerformProvisioningCommands(server, GeneralProvisionCommandPart);

        server.StatusLastChecked = DateTime.UtcNow;
        server.BumpUpdatedAt();
        await Database.SaveChangesAsync(cancellationToken);

        // If not provisioned yet, need to requeue this job
        if (!server.ProvisionedFully)
        {
            Logger.LogTrace("External server {Id} not yet fully provisioned", id);
            jobClient.Schedule<ProvisionControlledServerJob>(x => Execute(id, CancellationToken.None),
                TimeSpan.FromSeconds(10));
        }
        else
        {
            Logger.LogTrace("External server {Id} is provisioned now", id);
        }
    }

    protected override IBaseSSHAccess ConnectWithSSH(BaseServer server)
    {
        if (server.PublicAddress == null)
            throw new InvalidOperationException("Can't connect to a server with no public address");

        sshAccess.ConnectTo(server.PublicAddress.ToString(), ((ExternalServer)server).SSHKeyFileName);
        return sshAccess;
    }
}