namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   Used to keep track of which commands have been created and which need to be created on startup
/// </summary>
public class GlobalDiscordBotCommand
{
    public GlobalDiscordBotCommand(string registeredKey)
    {
        RegisteredKey = registeredKey;

        if (RegisteredKey.Length > 500)
            throw new ArgumentException("Key is too long");
    }

    [Key]
    public string RegisteredKey { get; set; }

    public static string GenerateKey(string command, int version)
    {
        return $"{command} v{version}";
    }
}
