namespace ThriveDevCenter.Server.Utilities;

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

public static class IPHelpers
{
    public static string PartlyAnonymizedIP(IPAddress ipAddress)
    {
        bool ipV6 = ipAddress.AddressFamily == AddressFamily.InterNetworkV6;

        var bytes = ipAddress.GetAddressBytes();

        var stringBuilder = new StringBuilder();

        var step = ipV6 ? 2 : 1;

        var anonymizationThreshold = bytes.Length / 2;

        // As the first half can be the exact address a consumer has at their house, we remove one extra part of the
        // address here
        if (ipV6)
            anonymizationThreshold -= step;

        for (int i = 0; i < bytes.Length; i += step)
        {
            if (i > 0)
            {
                stringBuilder.Append(ipV6 ? ':' : '.');
            }

            if (i >= anonymizationThreshold)
            {
                // Anonymized part
                if (ipV6)
                {
                    // Use the shorthand for all 0s
                    stringBuilder.Append(":0");
                    break;
                }

                stringBuilder.Append('0');
                continue;
            }

            if (ipV6)
            {
                stringBuilder.Append(Convert.ToHexString(bytes, i, 2).ToLowerInvariant());
            }
            else
            {
                stringBuilder.Append(Convert.ToString(bytes[i]));
            }
        }

        return stringBuilder.ToString();
    }
}
