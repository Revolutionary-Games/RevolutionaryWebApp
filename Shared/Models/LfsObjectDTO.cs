namespace ThriveDevCenter.Shared.Models
{
    public class LfsObjectDTO : ClientSideTimedModel
    {
        public string LfsOid { get; set; }

        public long Size { get; set; }
    }
}
