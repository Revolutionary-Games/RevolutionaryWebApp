namespace ThriveDevCenter.Server.Services;

using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Renci.SshNet;

/// <summary>
///   Access to remote servers where we just run jobs and updates (and occasionally reboot) but don't control their
///   creation or online status.
/// </summary>
public interface IExternalServerSSHAccess : IBaseSSHAccess
{
    public void ConnectTo(string address, string keyName);
    public bool IsValidKey(string name);
}

public class ExternalServerSSHAccess : BaseSSHAccess, IExternalServerSSHAccess
{
    private readonly string username;
    private readonly string basePath;

    public ExternalServerSSHAccess(IConfiguration configuration)
    {
        basePath = configuration["CI:ExternalSSHBasePath"] ?? string.Empty;
        username = configuration["CI:ExternalSSHUsername"] ?? string.Empty;

        if (string.IsNullOrEmpty(username))
            username = configuration["CI:SSHUsername"] ?? string.Empty;

        if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(username))
        {
            Configured = false;
            return;
        }

        Configured = true;
    }

    public void ConnectTo(string address, string keyName)
    {
        var keyAuth = new PrivateKeyAuthenticationMethod(username, new PrivateKeyFile(KeyNameToPath(keyName)));
        StartNewConnection(address, username, keyAuth);
    }

    public bool IsValidKey(string name)
    {
        var keyPath = KeyNameToPath(name);

        if (!File.Exists(keyPath))
            return false;

        try
        {
            _ = new PrivateKeyAuthenticationMethod(username, new PrivateKeyFile(keyPath));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string KeyNameToPath(string name)
    {
        return Path.Join(basePath, name);
    }
}
