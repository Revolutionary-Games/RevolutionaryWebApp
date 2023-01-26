namespace ThriveDevCenter.Server.Services;

using System;
using Microsoft.Extensions.Configuration;
using Renci.SshNet;

/// <summary>
///   Access to remote servers where we control the server creation, startup and shutdown using an SSH key
/// </summary>
public interface IControlledServerSSHAccess : IBaseSSHAccess
{
    public string SSHUsername { get; }

    public void ConnectTo(string address);
}

public class ControlledServerSSHAccess : BaseSSHAccess, IControlledServerSSHAccess
{
    private readonly PrivateKeyAuthenticationMethod? keyAuth;
    private readonly string username;

    public ControlledServerSSHAccess(IConfiguration configuration)
    {
        var keyFile = configuration["CI:SSHKeyFile"];
        username = configuration["CI:SSHUsername"] ?? string.Empty;

        if (string.IsNullOrEmpty(keyFile) || string.IsNullOrEmpty(username))
        {
            Configured = false;
            return;
        }

        keyAuth = new PrivateKeyAuthenticationMethod(username, new PrivateKeyFile(keyFile));

        Configured = true;
    }

    public string SSHUsername => username;

    public void ConnectTo(string address)
    {
        if (!Configured)
            throw new Exception("Not configured");

        StartNewConnection(address, username, keyAuth!);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            keyAuth?.Dispose();
        }

        base.Dispose(disposing);
    }
}
