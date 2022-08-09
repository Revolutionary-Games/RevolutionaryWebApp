namespace ThriveDevCenter.Server.Authorization;

using System;
using System.Security.Cryptography;
using SimpleBase;

public static class NonceGenerator
{
    public static string GenerateNonce(int bytes)
    {
        if (bytes is < 8 or > 128)
            throw new ArgumentException("invalid number of bytes to use for none");

        byte[] raw = new byte[bytes];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(raw);
        }

        return Base58.Bitcoin.Encode(raw);
    }
}