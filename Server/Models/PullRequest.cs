namespace ThriveDevCenter.Server.Models
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Text.Json;
    using Microsoft.EntityFrameworkCore;
    using Shared.Models.Enums;

    /// <summary>
    ///   Models the state of a pull request's state on the DevCenter side
    /// </summary>
    [Index(nameof(Repository), nameof(PullRequestIdentification), IsUnique = true)]
    public class PullRequest : UpdateableModel
    {
        [Required]
        public string Repository { get; set; }

        [Required]
        public string PullRequestIdentification { get; set; }

        public CLASignatureStatus ClaStatus { get; set; } = CLASignatureStatus.NotChecked;

        /// <summary>
        ///   Already posted PullRequestAutoComments, serialized as a JSON list of IDs
        /// </summary>
        public string PostedCommentsRaw { get; set; }

        [NotMapped]
        public IReadOnlyList<long> PostedComments
        {
            get
            {
                if (string.IsNullOrEmpty(PostedCommentsRaw))
                    return new List<long>();

                return JsonSerializer.Deserialize<List<long>>(PostedCommentsRaw);
            }
            set
            {
                if (value == null)
                {
                    PostedCommentsRaw = null;
                    return;
                }

                PostedCommentsRaw = JsonSerializer.Serialize(value);
            }
        }
    }
}
