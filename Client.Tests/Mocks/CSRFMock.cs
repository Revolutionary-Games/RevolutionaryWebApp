namespace ThriveDevCenter.Client.Tests.Mocks;

using Moq;
using Services;

public class CSRFMock : Mock<ICSRFTokenReader>
{
    public CSRFMock(long? userId = null, bool valid = true)
    {
        Setup(csrf => csrf.Token).Returns(valid ? "aabb" : string.Empty);
        Setup(csrf => csrf.Valid).Returns(valid);
        Setup(csrf => csrf.TimeRemaining).Returns(valid ? 10000 : -5);
        Setup(csrf => csrf.InitialUserId).Returns(userId);
    }
}