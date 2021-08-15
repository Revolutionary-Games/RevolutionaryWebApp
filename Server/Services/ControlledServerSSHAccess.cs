namespace ThriveDevCenter.Server.Services
{
    using System;
    using Microsoft.Extensions.Configuration;
    using Renci.SshNet;

    public class ControlledServerSSHAccess : BaseSSHAccess
    {
        private readonly PrivateKeyAuthenticationMethod keyAuth;
        private readonly string username;

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

        public void ConnectTo(string address)
        {
            StartNewConnection(address, username, keyAuth);
        }
    }
}
