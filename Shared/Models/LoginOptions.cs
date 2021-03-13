namespace ThriveDevCenter.Shared.Models
{
    using System.Collections.Generic;

    public class LoginOptions
    {
        public List<LoginOption> Options { get; set; }
    }

    public class LoginOption
    {
        public string ReadableName { get; set; }
        public string InternalName { get; set; }
    }
}
