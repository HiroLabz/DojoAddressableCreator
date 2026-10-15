using System;
using UnityEngine;

namespace Dojo.Framework.Auth
{
    /// <summary>
    /// What this client identifies itself as to WorkOS, and where its AuthKit domain is.
    /// </summary>
    /// <remarks>
    /// Serialized on the root scope beside <c>SpacetimeSettings</c>, and for the same reason:
    /// the service that reads it is not a <c>MonoBehaviour</c>, and filling these in for the
    /// first time (or pointing at a different WorkOS application) is worth more than the tidiness
    /// of a component.
    /// <para>
    /// <b>This targets a WorkOS Connect "OAuth Application," not the User Management application</b>
    /// hiro-ai-api's own console login uses — they're separate registrations in the WorkOS
    /// dashboard, even though (confirmed by inspecting an issued token) they resolve to the same
    /// underlying WorkOS user directory. The distinction matters here because only the Connect
    /// application's tokens are actually usable: SpacetimeDB verifies a token by fetching
    /// <c>{iss}/.well-known/openid-configuration</c> off of it, and User Management's tokens have
    /// <c>iss = https://api.workos.com/</c>, which doesn't serve that document (confirmed: 404).
    /// A Connect application's tokens have <c>iss</c> set to the AuthKit domain below, which does
    /// (confirmed: this repo's own local SpacetimeDB instance accepts one and returns a real
    /// identity). See <c>server/README.md</c> for the full story.
    /// </para>
    /// <para>
    /// Unlike the old User Management endpoints, a Connect application's OAuth endpoints are
    /// <em>not</em> a fixed host for every WorkOS customer — they live under this specific
    /// application's AuthKit domain, which differs per WorkOS environment (a sandbox/staging
    /// domain here is not the same as the production one), hence <see cref="AuthKitDomain"/> is a
    /// field rather than a constant.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class WorkOSSettings
    {
        [Tooltip("The client id of the WorkOS Connect OAuth Application (Public, PKCE) — not the " +
                 "User Management application's client id. From the WorkOS dashboard.")]
        [SerializeField] string clientId = string.Empty;

        [Tooltip("This WorkOS environment's AuthKit domain, exactly as the dashboard shows it " +
                 "(e.g. \"grand-lamp-76-staging.authkit.app\"), no scheme or trailing slash. " +
                 "The OAuth Application's authorize/token endpoints live here.")]
        [SerializeField] string authKitDomain = string.Empty;

        [Tooltip("The custom URI scheme registered on every mobile platform this builds for, " +
                 "that receives the OAuth redirect back into the app. Must exactly match a " +
                 "redirect URI registered on this application in the WorkOS dashboard. Not used " +
                 "in the Editor or a Standalone build — see desktopRedirectPort.")]
        [SerializeField] string redirectUri = "dojo://auth-callback";

        [Tooltip("Loopback port used for the OAuth redirect in the Editor and Standalone builds, " +
                 "where there is no installed app for the OS to hand redirectUri's custom scheme " +
                 "back to. Register http://127.0.0.1:<this port>/callback as a second redirect " +
                 "URI on the WorkOS application too.")]
        [SerializeField] int desktopRedirectPort = 51820;

        [Tooltip("Seconds to wait for the player to complete sign-in in the system browser " +
                 "before giving up and carrying on without one.")]
        [SerializeField] float signInTimeoutSeconds = 120f;

        public string ClientId => clientId?.Trim() ?? string.Empty;

        /// <summary>The AuthKit domain, trimmed and with any pasted-in scheme or trailing slash stripped.</summary>
        public string AuthKitDomain => (authKitDomain ?? string.Empty)
            .Trim()
            .TrimEnd('/')
            .Replace("https://", string.Empty)
            .Replace("http://", string.Empty);

        public string RedirectUri => string.IsNullOrWhiteSpace(redirectUri)
            ? "dojo://auth-callback"
            : redirectUri.Trim();

        /// <summary>Where the loopback listener binds in the Editor and Standalone builds.</summary>
        public int DesktopRedirectPort => desktopRedirectPort > 0 ? desktopRedirectPort : 51820;

        /// <summary>
        /// How long to wait for an interactive sign-in before carrying on without one.
        /// </summary>
        /// <remarks>
        /// Floored rather than trusted, for the same reason as <c>SpacetimeSettings.ConnectTimeoutSeconds</c>
        /// — a zero left in the Inspector would time out before the browser even finished opening.
        /// </remarks>
        public float SignInTimeoutSeconds => signInTimeoutSeconds < 5f ? 120f : signInTimeoutSeconds;

        /// <summary>
        /// False until both the client id and the AuthKit domain are filled in.
        /// </summary>
        /// <remarks>
        /// A configuration state, not an error. A player is never blocked on this being false;
        /// sign-in is simply unavailable and the connection falls back to an anonymous identity.
        /// </remarks>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(AuthKitDomain);
    }
}
