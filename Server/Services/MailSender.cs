namespace ThriveDevCenter.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Jobs;
    using MailKit.Net.Smtp;
    using MailKit.Security;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using MimeKit;

    public class MailSender : IMailSender
    {
        private readonly ILogger<MailSender> logger;
        private readonly string fromAddress;
        private readonly string senderName;
        private readonly string emailPassword;
        private readonly string host;
        private readonly short port;
        private readonly bool requireTls;

        public MailSender(ILogger<MailSender> logger, IConfiguration configuration)
        {
            this.logger = logger;

            fromAddress = configuration["Email:FromAddress"];
            senderName = configuration["Email:Name"];
            emailPassword = configuration["Email:Password"];
            host = configuration["Email:Host"];
            port = Convert.ToInt16(configuration["Email:Port"]);
            requireTls = Convert.ToBoolean(configuration["Email:RequireTls"]);

            if (string.IsNullOrEmpty(fromAddress) || string.IsNullOrEmpty(senderName) || string.IsNullOrEmpty(host) ||
                port == 0)
            {
                Configured = false;
                return;
            }

            Configured = true;
        }

        public bool Configured { get; }

        public virtual async Task SendEmail(MailRequest request, CancellationToken cancellationToken)
        {
            ThrowIfNotConfigured();

            var sender = MailboxAddress.Parse(fromAddress);
            sender.Name = senderName;

            var builder = new BodyBuilder { HtmlBody = request.HtmlBody, TextBody = request.PlainTextBody };
            var email = new MimeMessage
            {
                Sender = sender,
                Subject = request.Subject,
                Body = builder.ToMessageBody(),
            };

            email.From.Add(sender);

            email.To.Add(MailboxAddress.Parse(request.Recipient));

            // TODO: batching of email send requests (or perhaps queue sender jobs would be better for this)

            using var smtp = new SmtpClient();

            SecureSocketOptions connectMode = SecureSocketOptions.StartTls;

            if (!requireTls)
                connectMode = SecureSocketOptions.StartTlsWhenAvailable;

            // When using ssl port, switch to SSL mode
            if (port == 465)
                connectMode = SecureSocketOptions.SslOnConnect;

            await smtp.ConnectAsync(host, port, connectMode, cancellationToken);

            // Only authenticate if we have a password set
            if (!string.IsNullOrEmpty(emailPassword))
                await smtp.AuthenticateAsync(fromAddress, emailPassword, cancellationToken);

            logger.LogInformation("Sending email to {Recipient}", request.Recipient);
            await smtp.SendAsync(email, cancellationToken);

            // If this is canceled here, then it might be possible that a single email is sent twice, but should be
            // very rare
            await smtp.DisconnectAsync(true, cancellationToken);
        }

        protected void ThrowIfNotConfigured()
        {
            if (!Configured)
                throw new InvalidOperationException("Email is not configured");
        }
    }

    /// <summary>
    ///   Sends mails from a background operation
    /// </summary>
    public class MailToQueueSender : MailSender, IMailQueue
    {
        private readonly ILogger<MailToQueueSender> logger;
        private readonly IBackgroundJobClient jobClient;

        public MailToQueueSender(ILogger<MailToQueueSender> logger, IConfiguration configuration,
            IBackgroundJobClient jobClient) : base(logger,
            configuration)
        {
            this.logger = logger;
            this.jobClient = jobClient;
        }

        public override Task SendEmail(MailRequest request, CancellationToken cancellationToken)
        {
            ThrowIfNotConfigured();

            jobClient.Enqueue<SendSingleQueuedEmailJob>(x => x.Execute(request, CancellationToken.None));
            return Task.CompletedTask;
        }
    }

    public interface IMailSender
    {
        bool Configured { get; }

        Task SendEmail(MailRequest request, CancellationToken cancellationToken);
    }

    public interface IMailQueue : IMailSender
    {
    }

    public class MailRequest
    {
        public string Recipient { get; set; }
        public string Cc { get; set; }
        public string Subject { get; set; }
        public string HtmlBody { get; set; }
        public string PlainTextBody { get; set; }
    }
}
