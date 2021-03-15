namespace ThriveDevCenter.Server.Authorization
{
    using System;
    using System.Security.Cryptography;
    using Microsoft.AspNetCore.Cryptography.KeyDerivation;

    public class Passwords
    {
        // 16 bytes of salt
        private const int SaltLength = 16;
        private const int HashingIterations = 10000;

        /// <summary>
        ///   Even though sha1 is used, maybe this being longer helps...
        /// </summary>
        private const int HashBits = 384;

        /// <summary>
        ///   If you develop your own app based on ThriveDevCenter you'll want to change this
        /// </summary>
        private static readonly byte[] Pepper = { 49, 227, 73, 64, 254, 28, 52, 20 };

        public static string CreateSaltedPasswordHash(string password)
        {
            byte[] salt = new byte[SaltLength];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            return CreateSaltedPasswordHash(password, salt);
        }

        public static string CreateSaltedPasswordHash(string password, byte[] salt)
        {
            var base64Salt = Convert.ToBase64String(salt);

            byte[] combined = new byte[salt.Length + Pepper.Length];

            Array.Copy(salt, 0, combined, 0, salt.Length);
            Array.Copy(Pepper, 0, combined, salt.Length, Pepper.Length);

            return base64Salt + ":" + Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password,
                combined,
                KeyDerivationPrf.HMACSHA256,
                HashingIterations,
                HashBits / 8));
        }
    }
}
