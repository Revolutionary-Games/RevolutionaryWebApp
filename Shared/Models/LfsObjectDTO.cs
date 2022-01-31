namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;

    public class LfsObjectDTO : ClientSideTimedModel
    {
        [Required]
        public string LfsOid { get; set; } = string.Empty;

        public long Size { get; set; }
    }
}
