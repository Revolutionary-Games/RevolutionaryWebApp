namespace ThriveDevCenter.Server.Services;

using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Renci.SshNet;

public class ExternalServerSSHAccess : BaseSSHAccess, IExternalServerSSHAccess
{
    private readonly string username;
    private readonly string basePath;

    public ExternalServerSSHAccess(IConfiguration configuration)
    {
        basePath = configuration["CI:ExternalSSHBasePath"];
        username = configuration["CI:ExternalSSHUsername"];

        if (string.IsNullOrEmpty(username))
            username = configuration["CI:SSHUsername"];

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
            var _ = new PrivateKeyAuthenticationMethod(username, new PrivateKeyFile(keyPath));
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

public interface IExternalServerSSHAccess : IBaseSSHAccess
{
    void ConnectTo(string address, string keyName);
    bool IsValidKey(string name);
}