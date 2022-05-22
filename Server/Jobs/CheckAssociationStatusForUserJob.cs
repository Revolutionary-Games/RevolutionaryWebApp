namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Models;

public class CheckAssociationStatusForUserJob
{
    private readonly ILogger<CheckAssociationStatusForUserJob> logger;
    private readonly NotificationsEnabledDb database;

    public CheckAssociationStatusForUserJob(ILogger<CheckAssociationStatusForUserJob> logger,
        NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(string email, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
