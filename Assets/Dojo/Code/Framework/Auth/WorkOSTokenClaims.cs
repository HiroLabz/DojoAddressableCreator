using System;
using System.Text;
using UnityEngine;

namespace Dojo.Framework.Auth
{
    /// <summary>Reads the claims the game needs out of a WorkOS token: who signed in.</summary>
    /// <remarks>
    /// For display only - the Lobby's session panel. The signature is not checked: the tokens came
    /// straight back from WorkOS's own token endpoint over TLS, and nothing trusts them for access
    /// here. SpacetimeDB and Hiro check the access token themselves.
    /// </remarks>
    public static class WorkOSTokenClaims
    {
        /// <summary>The <c>email</c> claim of an id_token, or "" when the token is missing, malformed or has none.</summary>
        public static string EmailOf(string idToken)
        {
            var claims = Read(idToken);
            return claims != null && claims.email != null ? claims.email : string.Empty;
        }

        static Claims Read(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            try
            {
                // base64url, unpadded: back to plain base64 before decoding.
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

                return JsonUtility.FromJson<Claims>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            }
            catch (Exception)
            {
                return null;
            }
        }

        [Serializable]
        sealed class Claims
        {
            public string email;
        }
    }
}
