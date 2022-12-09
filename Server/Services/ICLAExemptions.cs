namespace ThriveDevCenter.Server.Services;

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

/// <summary>
///   Provides info on which users (on Github) are exempt from having to sign the CLA
/// </summary>
public interface ICLAExemptions
{
    public bool IsExempt(string username);
}

public class CLAExemptions : ICLAExemptions
{
    private readonly List<string> exemptions;

    public CLAExemptions(IConfiguration configuration)
    {
        exemptions = configuration.GetSection("CLA:ExemptGithubUsers").Get<List<string>>();
    }

    public bool IsExempt(string username)
    {
        return exemptions.Contains(username);
    }
}
