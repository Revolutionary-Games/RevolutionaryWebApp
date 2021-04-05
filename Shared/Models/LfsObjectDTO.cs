namespace ThriveDevCenter.Shared.Models
{
    public class LfsObjectDTO : ClientSideTimedModel
    {
        public string LfsOid { get; set; }

        public int Size { get; set; }
    }
}
