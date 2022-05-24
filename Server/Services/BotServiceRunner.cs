namespace ThriveDevCenter.Server.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public class BotServiceRunner : BackgroundService
{
    private readonly IServiceProvider serviceProvider;

    public BotServiceRunner(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();
        var revolutionaryBotService = scope.ServiceProvider
            .GetRequiredService<RevolutionaryDiscordBotService>();

        await revolutionaryBotService.ExecuteAsync(stoppingToken);
    }
}
