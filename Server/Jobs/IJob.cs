namespace ThriveDevCenter.Server.Jobs
{
    using System.Threading.Tasks;

    public interface IJob
    {
        public Task Execute();
    }
}
