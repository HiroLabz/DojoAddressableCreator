using System;
using System.Globalization;
using UnityEngine;

namespace Dojo.Framework.Auth
{
    /// <summary>
    /// Persists the WorkOS token pair between runs, the same way the SpacetimeDB SDK's own
    /// <c>AuthToken</c> persists its token — PlayerPrefs, under a key nothing else uses.
    /// </summary>
    /// <remarks>
    /// A distinct store rather than reusing <c>AuthToken</c>, because these are two different
    /// credentials for two different systems: <c>AuthToken</c> holds what SpacetimeDB issued
    /// <em>us</em> after connecting, and this holds what WorkOS issued the <em>player</em> before
    /// we ever try to connect. Conflating the two keys would mean a change to one system's token
    /// lifecycle silently corrupting the other's.
    /// </remarks>
    static class WorkOSTokenStore
    {
        const string AccessTokenKey = "dojo.workos.access_token";
        const string RefreshTokenKey = "dojo.workos.refresh_token";
        const string ExpiresAtKey = "dojo.workos.expires_at_unix";
        const string EmailKey = "dojo.workos.email";

        /// <summary>
        /// Skew subtracted from the stored expiry, so a token that is about to expire is treated
        /// as already expired rather than handed to a caller who will have it rejected mid-use.
        /// </summary>
        const long ExpirySkewSeconds = 30;

        internal static string AccessToken => PlayerPrefs.GetString(AccessTokenKey, string.Empty);

        internal static string RefreshToken => PlayerPrefs.GetString(RefreshTokenKey, string.Empty);

        /// <summary>The signed-in player's email, from the last id_token that carried one.</summary>
        internal static string Email => PlayerPrefs.GetString(EmailKey, string.Empty);

        /// <summary>
        /// The moment <see cref="HasValidAccessToken"/> turns false — the recorded expiry with the
        /// skew already taken off. <see cref="DateTimeOffset.MinValue"/> when there is nothing
        /// usable stored.
        /// </summary>
        /// <remarks>
        /// The skew is included on purpose. A refresher scheduling against the raw expiry would
        /// wake up after the token had already stopped being handed out, which is the gap it exists
        /// to prevent.
        /// </remarks>
        internal static DateTimeOffset ExpiresAt
        {
            get
            {
                if (string.IsNullOrEmpty(AccessToken))
                {
                    return DateTimeOffset.MinValue;
                }

                var raw = PlayerPrefs.GetString(ExpiresAtKey, string.Empty);

                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresAtUnix))
                {
                    return DateTimeOffset.MinValue;
                }

                return DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix)
                    - TimeSpan.FromSeconds(ExpirySkewSeconds);
            }
        }

        /// <summary>True once there is an access token that has not expired, skew included.</summary>
        internal static bool HasValidAccessToken
        {
            get
            {
                if (string.IsNullOrEmpty(AccessToken))
                {
                    return false;
                }

                var raw = PlayerPrefs.GetString(ExpiresAtKey, string.Empty);

                // No expiry recorded reads as "cannot tell, so do not trust it" rather than
                // "unlimited" — a token whose lifetime we never captured should be refreshed or
                // re-earned, not treated as good forever by omission.
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresAtUnix))
                {
                    return false;
                }

                var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix);
                return DateTimeOffset.UtcNow < expiresAt - TimeSpan.FromSeconds(ExpirySkewSeconds);
            }
        }

        /// <summary>Records a fresh token pair. <paramref name="refreshToken"/> may be empty if WorkOS did not issue one.</summary>
        internal static void Save(string accessToken, string refreshToken, DateTimeOffset expiresAt)
        {
            PlayerPrefs.SetString(AccessTokenKey, accessToken ?? string.Empty);
            PlayerPrefs.SetString(RefreshTokenKey, refreshToken ?? string.Empty);
            PlayerPrefs.SetString(ExpiresAtKey, expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Records who signed in. An empty email is ignored rather than stored: a refresh that
        /// brings no id_token must not forget a player it has not changed.
        /// </summary>
        internal static void SaveEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return;
            }

            PlayerPrefs.SetString(EmailKey, email);
            PlayerPrefs.Save();
        }

        /// <summary>Forgets the stored tokens, so the next request re-runs interactive sign-in.</summary>
        internal static void Clear()
        {
            PlayerPrefs.DeleteKey(AccessTokenKey);
            PlayerPrefs.DeleteKey(RefreshTokenKey);
            PlayerPrefs.DeleteKey(ExpiresAtKey);
            PlayerPrefs.DeleteKey(EmailKey);
        }
    }
}
