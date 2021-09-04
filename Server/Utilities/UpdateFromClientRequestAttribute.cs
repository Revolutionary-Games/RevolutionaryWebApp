namespace ThriveDevCenter.Server.Utilities
{
    using System;

    [AttributeUsage(AttributeTargets.Property)]
    public class UpdateFromClientRequestAttribute : Attribute
    {
        /// <summary>
        ///   If not null, overrides the property where an update is looked for
        /// </summary>
        public string RequestPropertyName { get; set; }
    }
}
