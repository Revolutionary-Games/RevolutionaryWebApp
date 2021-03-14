namespace ThriveDevCenter.Shared.Models
{
    using System.Collections.Generic;

    public class LoginOptions
    {
        public List<LoginCategory> Categories { get; set; }
    }

    public class LoginCategory
    {
        public string Name { get; set; }
        public List<LoginOption> Options { get; set; }
    }

    public class LoginOption
    {
        public string ReadableName { get; set; }
        public string InternalName { get; set; }
        public bool Active { get; set; } = true;

        public bool Local { get; set; } = false;
    }
}
