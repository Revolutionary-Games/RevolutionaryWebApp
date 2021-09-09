namespace ThriveDevCenter.Server.Services
{
    using System;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Utilities;

    public class GithubCommitStatusReporter : GithubAPI, IGithubCommitStatusReporter
    {
        private readonly Uri baseUrl;

        public GithubCommitStatusReporter(ILogger<GithubCommitStatusReporter> logger, IConfiguration configuration) :
            base(logger, configuration["CI:StatusReporting:OAuthToken"])
        {
            try
            {
                baseUrl = configuration.GetBaseUrl();
            }
            catch (Exception e)
            {
                logger.LogError("Can't get base url for Github status reporting: {@E}", e);
            }
        }

        public string CreateStatusUrlForJob(CiJob job)
        {
            if (baseUrl == null)
                throw new InvalidOperationException("Base URL is not set");

            return new Uri(baseUrl, $"/ci/{job.CiProjectId}/build/{job.CiBuildId}/jobs/{job.CiJobId}").ToString();
        }

        public string CreateStatusUrlForCLA()
        {
            if (baseUrl == null)
                throw new InvalidOperationException("Base URL is not set");

            return new Uri(baseUrl, "/cla").ToString();
        }
    }

    public interface IGithubCommitStatusReporter : IGithubAPI
    {
        string CreateStatusUrlForJob(CiJob job);
        string CreateStatusUrlForCLA();
    }
}
