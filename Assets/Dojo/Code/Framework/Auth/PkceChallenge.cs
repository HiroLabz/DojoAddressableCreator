using System;
using System.Security.Cryptography;
using System.Text;

namespace Dojo.Framework.Auth
{
    /// <summary>
    /// The PKCE verifier/challenge pair and the anti-forgery <c>state</c> value an authorize
    /// request needs.
    /// </summary>
    /// <remarks>
    /// Unlike the AuthKit endpoint URLs, this part of the flow is not WorkOS-specific — RFC 7636
    /// fixes the algorithm exactly, so it is safe to implement without confirming anything against
    /// WorkOS's own reference.
    /// </remarks>
    static class PkceChallenge
    {
        /// <summary>A generated verifier and the S256 challenge derived from it.</summary>
        internal readonly struct Pair
        {
            internal readonly string Verifier;
            internal readonly string Challenge;

            internal Pair(string verifier, string challenge)
            {
                Verifier = verifier;
                Challenge = challenge;
            }
        }

        /// <summary>
        /// A fresh verifier/challenge pair.
        /// </summary>
        /// <remarks>
        /// The verifier is 32 random bytes, base64url-encoded — 43 characters, within RFC 7636's
        /// required 43-128 range. The challenge is the base64url-encoded SHA-256 of the verifier's
        /// ASCII bytes, exactly as the spec defines the <c>S256</c> method.
        /// </remarks>
        internal static Pair Create()
        {
            var verifier = Base64UrlEncode(RandomBytes(32));

            using (var sha256 = SHA256.Create())
            {
                var challenge = Base64UrlEncode(sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
                return new Pair(verifier, challenge);
            }
        }

        /// <summary>A fresh anti-forgery value for the authorize request's <c>state</c> parameter.</summary>
        internal static string NewState() => Base64UrlEncode(RandomBytes(16));

        static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return bytes;
        }

        /// <summary>Base64, but URL-safe and with the padding RFC 7636 forbids stripped.</summary>
        static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
