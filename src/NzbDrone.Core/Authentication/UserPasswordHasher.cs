using System;
using System.Security.Cryptography;
using System.Text;

namespace NzbDrone.Core.Authentication
{
    internal static class UserPasswordHasher
    {
        // This version fixes the PRF, work factor and payload lengths.
        private const string Prefix = "pbkdf2-sha512-v1$";
        private const int Iterations = 220_000;
        private const int SaltLength = 16;
        private const int SubkeyLength = 32;
        private const int EncodedPayloadLength = 64;
        private const int LegacyHashLength = 64;

        public static string Hash(string password)
        {
            Span<byte> payload = stackalloc byte[SaltLength + SubkeyLength];
            RandomNumberGenerator.Fill(payload[..SaltLength]);
            Derive(password, payload[..SaltLength], payload[SaltLength..]);
            return Prefix + Convert.ToBase64String(payload);
        }

        public static bool Verify(string storedHash, string password, out bool needsUpgrade)
        {
            needsUpgrade = false;
            if (storedHash == null)
            {
                return false;
            }

            if (storedHash.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return VerifyCurrent(storedHash.AsSpan(Prefix.Length), password);
            }

            // Older releases stored a lowercase SHA-256 digest. Only verification uses this format.
            if (storedHash.Length != LegacyHashLength)
            {
                return false;
            }

            needsUpgrade = CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(storedHash), Encoding.ASCII.GetBytes(password.SHA256Hash()));
            return needsUpgrade;
        }

        private static bool VerifyCurrent(ReadOnlySpan<char> encodedPayload, string password)
        {
            Span<byte> payload = stackalloc byte[SaltLength + SubkeyLength];
            if (encodedPayload.Length != EncodedPayloadLength ||
                !Convert.TryFromBase64Chars(encodedPayload, payload, out var written) ||
                written != payload.Length)
            {
                return false;
            }

            Span<byte> expected = stackalloc byte[SubkeyLength];
            Derive(password, payload[..SaltLength], expected);
            return CryptographicOperations.FixedTimeEquals(payload[SaltLength..], expected);
        }

        private static void Derive(string password, ReadOnlySpan<byte> salt, Span<byte> key)
        {
            Rfc2898DeriveBytes.Pbkdf2(password, salt, key, Iterations, HashAlgorithmName.SHA512);
        }
    }
}
