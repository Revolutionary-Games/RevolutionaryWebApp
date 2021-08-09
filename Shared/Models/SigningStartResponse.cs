namespace ThriveDevCenter.Shared.Models
{
    public class SigningStartResponse
    {
        public bool SessionStarted { get; set; }

        /// <summary>
        ///   Tells where the client should go next
        /// </summary>
        public string NextPath { get; set; }
    }
}
