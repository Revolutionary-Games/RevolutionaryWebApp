namespace ThriveDevCenter.Server.Models
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    ///   DevCenter side info for a pull request
    /// </summary>
    public class GithubPullRequest : UpdateableModel
    {
        [Required]
        public string Repository { get; set; }

        public long GithubId { get; set; }

        public bool Open { get; set; } = true;

        [Required]
        public string AuthorUsername { get; set; }

        /// <summary>
        ///   Latest commit from this PR to set the commit statuses on
        /// </summary>
        [Required]
        public string LatestCommit { get; set; }

        /// <summary>
        ///   Status of cla signature for the author
        /// </summary>
        public bool? ClaSigned { get; set; }

        public ICollection<GithubAutoComment> AutoComments { get; set; } = new HashSet<GithubAutoComment>();
    }
}
