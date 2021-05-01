namespace ThriveDevCenter.Shared.Models
{
    public class UserToken
    {
        public string CSRF { get; set; }
        public UserInfo User { get; set; }
    }
}
