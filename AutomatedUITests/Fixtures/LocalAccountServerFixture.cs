namespace AutomatedUITests.Fixtures;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RevolutionaryWebApp.Server;
using RevolutionaryWebApp.Server.Services;

public class LocalAccountServerFixture : WebHostServerFixture<Startup>
{
    public CapturingMailQueue MailQueue { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IMailQueue>();
        services.AddSingleton<IMailQueue>(MailQueue);
    }

    public sealed class CapturingMailQueue : IMailQueue
    {
        private readonly TaskCompletionSource<MailRequest> sentEmail =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Configured => true;

        public Task SendEmail(MailRequest request, CancellationToken cancellationToken)
        {
            sentEmail.TrySetResult(request);
            return Task.CompletedTask;
        }

        public Task<MailRequest> WaitForEmail()
        {
            return sentEmail.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }
}
