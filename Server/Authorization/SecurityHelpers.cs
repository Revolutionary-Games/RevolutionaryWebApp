namespace RevolutionaryWebApp.Server.Authorization;

public static class SecurityHelpers
{
    public static bool SlowEquals(string? a, string? b)
    {
        if (a == null || b == null)
            return a == b;

        uint diff = (uint)a.Length ^ (uint)b.Length;

        for (int i = 0; i < a.Length && i < b.Length; ++i)
            diff |= (uint)(a[i] ^ b[i]);

        return diff == 0;
    }

    public static bool SlowEquals(byte[]? a, byte[]? b)
    {
        if (a == null || b == null)
            return a == b;

        uint diff = (uint)a.Length ^ (uint)b.Length;

        for (int i = 0; i < a.Length && i < b.Length; ++i)
            diff |= (uint)(a[i] ^ b[i]);

        return diff == 0;
    }
}
