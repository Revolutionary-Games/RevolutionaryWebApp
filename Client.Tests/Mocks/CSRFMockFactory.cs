namespace RevolutionaryWebApp.Client.Tests.Mocks;

using NSubstitute;
using Services;

public static class CSRFMockFactory
{
    public static ICSRFTokenReader Create(long? userId = null, bool valid = true)
    {
        var mock = Substitute.For<ICSRFTokenReader>();

        mock.Token.Returns(valid ? "aabb" : string.Empty);
        mock.Valid.Returns(valid);
        mock.TimeRemaining.Returns(valid ? 10000 : -5);
        mock.InitialUserId.Returns(userId);

        return mock;
    }
}
