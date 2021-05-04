namespace ThriveDevCenter.Server.Services
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Configuration;
    using Renci.SshNet;

    public class ControlledServerSSHAccess : IDisposable
    {
        private readonly PrivateKeyAuthenticationMethod keyAuth;
        private readonly string username;

        private SshClient client;

        public ControlledServerSSHAccess(IConfiguration configuration)
        {
            var keyFile = configuration["CI:SSHKeyFile"];
            username = configuration["CI:SSHUsername"];

            if (string.IsNullOrEmpty(keyFile) || string.IsNullOrEmpty(username))
            {
                Configured = false;
                return;
            }

            keyAuth = new PrivateKeyAuthenticationMethod(username, new PrivateKeyFile(keyFile));

            Configured = true;
        }

        public bool Configured { get; }

        public void ConnectTo(string address)
        {
            client?.Dispose();

            var connectionInfo = new ConnectionInfo(address, username, keyAuth);

            client = new SshClient(connectionInfo);

            // TODO: is there a way to verify the other side fingerprint?

            client.Connect();
        }

        // TODO: add more async stuff in this class

        public CommandResult RunCommand(string commandStr)
        {
            using var command = client.CreateCommand(commandStr);

            command.CommandTimeout = TimeSpan.FromMinutes(10);
            var result = command.Execute();

            // Result is the command output
            // Error is maybe the error stream?

            return new CommandResult()
            {
                ExitCode = command.ExitStatus,
                Error = command.Error,
                Result = result
            };
        }

        public void Dispose()
        {
            client?.Dispose();
        }

        protected void ThrowIfNotConfigured()
        {
            if (!Configured)
                throw new Exception("SSH access to EC2 started servers is not configured");
        }

        public class CommandResult
        {
            public int ExitCode { get; set; }

            public bool Success => ExitCode == 0;

            public string Error { get; set; }
            public string Result { get; set; }
        }
    }
}
