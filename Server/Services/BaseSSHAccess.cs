namespace ThriveDevCenter.Server.Services
{
    using System;
    using Renci.SshNet;
    using Renci.SshNet.Common;

    public class BaseSSHAccess : IDisposable, IBaseSSHAccess
    {
        protected SshClient client { get; set; }
        public bool Configured { get; protected set; }

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

        public void Reboot()
        {
            try
            {
                var result = RunCommand("sudo reboot");
                if (!result.Success)
                {
                    throw new SshException(
                        $"Reboot command failed: {result.Result}, error: {result.Error}, code: {result.ExitCode}");
                }
            }
            catch (SshConnectionException e)
            {
                if (e.Message.Contains("established connection was aborted by the server"))
                    return;

                throw;
            }
        }

        // TODO: add more async stuff in this class

        public void Dispose()
        {
            client?.Dispose();
        }

        protected void StartNewConnection(string address, string username, PrivateKeyAuthenticationMethod auth)
        {
            client?.Dispose();

            var connectionInfo = new ConnectionInfo(address, username, auth)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            client = new SshClient(connectionInfo);

            // TODO: is there a way to verify the other side fingerprint?

            client.Connect();
        }

        protected void ThrowIfNotConfigured()
        {
            if (!Configured)
                throw new Exception("SSH access to external / controlled server is not configured");
        }

        // TODO: move out of this class
        public class CommandResult
        {
            public int ExitCode { get; set; }

            public bool Success => ExitCode == 0;

            public string Error { get; set; }
            public string Result { get; set; }
        }
    }

    public interface IBaseSSHAccess
    {
        bool Configured { get; }
        BaseSSHAccess.CommandResult RunCommand(string commandStr);

        /// <summary>
        ///   Reboots the server. Must be connected first
        /// </summary>
        /// <exception cref="SshException">If the reboot command fails</exception>
        public void Reboot();
    }
}
