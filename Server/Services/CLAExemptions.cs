namespace ThriveDevCenter.Server.Services;

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

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

public interface ICLAExemptions
{
    bool IsExempt(string username);
}