namespace ThriveDevCenter.Server.Tests.Authorization.Tests
{
    using Server.Authorization;
    using Xunit;

    public class PasswordHashingTests
    {
        [Fact]
        public void PasswordHashing_KnownPasswordHashMatches()
        {
            byte[] salt = { 1, 2, 3, 4, 5, 6, 7, 8 };

            // Hashed string has nonsense in it
            // ReSharper disable StringLiteralTypo
            Assert.Equal("AQIDBAUGBwg=:9CtRsYKuet+gr6NRVnrIjd37nKwH1sTCEI3kdt8i5N0oJF+n1JUR3Idy2SuU1+zi",
                Passwords.CreateSaltedPasswordHash("test1234", salt));
            // ReSharper enable StringLiteralTypo
        }

        [Fact]
        public void PasswordHashing_DifferentSaltsHaveDifferentResults()
        {
            byte[] salt1 = { 1, 2, 3, 4, 5, 6, 7, 8 };
            byte[] salt2 = { 2, 2, 3, 4, 5, 6, 7, 8 };

            Assert.NotEqual(Passwords.CreateSaltedPasswordHash("test1234", salt1),
                Passwords.CreateSaltedPasswordHash("test1234", salt2));
        }

        [Fact]
        public void PasswordHashing_AutomaticallyGeneratedSaltsAreDifferent()
        {
            var result1 = Passwords.CreateSaltedPasswordHash("test1234");
            var result2 = Passwords.CreateSaltedPasswordHash("test1234");
            var result3 = Passwords.CreateSaltedPasswordHash("test1234");

            if (result1 == result2 && result2 == result3)
                Assert.True(false, "subsequently created hashes without set salt should be different");
        }

        [Fact]
        public void PasswordHashing_PasswordVerificationWorks()
        {
            var hashed = Passwords.CreateSaltedPasswordHash("test1234");

            Assert.True(Passwords.CheckPassword(hashed, "test1234"));
            Assert.False(Passwords.CheckPassword(hashed, "test12345"));
            Assert.False(Passwords.CheckPassword(hashed, "test123"));
        }
    }
}
