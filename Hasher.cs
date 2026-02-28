using System.Security.Cryptography;

namespace P2PShare.Server
{
    public static class Hasher
    {
        private static readonly int _iterations = 300000, _hashLength = 32;
        private static readonly HashAlgorithmName _algorithm = HashAlgorithmName.SHA512;
        private static readonly char _separator = '-';

        public static string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);

            return $"{Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, _algorithm, _hashLength))}{_separator}{Convert.ToHexString(salt)}";
        }

        public static bool Verify(string password, string hashAndSalt)
        {
            var hashAndSaltSplit = hashAndSalt.Split(_separator);

            return CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromHexString(hashAndSaltSplit[1]), _iterations, _algorithm, _hashLength), Convert.FromHexString(hashAndSaltSplit[0]));
        }
    }
}
