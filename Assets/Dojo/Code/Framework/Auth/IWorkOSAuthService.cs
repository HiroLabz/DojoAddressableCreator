using System;
using System.Threading;
using UnityEngine;

namespace Dojo.Framework.Auth
{
    /// <summary>
    /// Gets a WorkOS-issued token for the signed-in player, signing them in if necessary.
    /// </summary>
    /// <remarks>
    /// One method, deliberately shaped like a value rather than an event: a caller that needs the
    /// player's identity awaits this and gets either a usable token or an empty string, and never
    /// has to know whether that meant "already signed in", "just signed in", or "declined/failed
    /// and we are carrying on without one" — those are this service's business, not its caller's.
    /// </remarks>
    public interface IWorkOSAuthService
    {
        /// <summary>
        /// Returns a usable WorkOS token: a stored one, a refreshed one, or — only when
        /// <paramref name="interactive"/> is true — one from an interactive browser sign-in.
        /// </summary>
        /// <remarks>
        /// Never throws. WorkOS not being configured, a non-interactive call with nothing to reuse,
        /// an interactive sign-in that times out or is cancelled, and a token exchange that fails,
        /// all come back as an empty string — the same "carry on without it" contract
        /// <c>ISpacetimeConnection.ConnectAsync</c> already has for an unreachable host.
        /// </remarks>
        Awaitable<string> GetTokenAsync(CancellationToken cancellationToken = default, bool interactive = false);

        /// <summary>
        /// Whether a browser sign-in can happen at all. False when WorkOS is not configured, or
        /// there is no backend to sign in to.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// The signed-in player's email, from the last sign-in; "" when nobody is signed in, or
        /// WorkOS did not say. For showing who is signed in, never for deciding access.
        /// </summary>
        string PlayerEmail { get; }

        /// <summary>
        /// Signs the player in through the browser, opened on <paramref name="route"/>'s page.
        /// Returns a stored or refreshed token instead, with no browser, if there is one.
        /// </summary>
        /// <remarks>
        /// Never throws, like <see cref="GetTokenAsync"/>: cancelled, timed out and refused all come
        /// back as "". <paramref name="email"/> is only a hint for the page; the player can change it.
        /// </remarks>
        Awaitable<string> SignInAsync(SignInRoute route, string email = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Whatever valid token is cached right now, or "" — never refreshes, never signs in,
        /// never touches the network.
        /// </summary>
        /// <remarks>
        /// For callers that need a synchronous read, chiefly <c>IApiSettings.ApiKey</c>
        /// implementations: that interface is a plain property by design (see its own remarks —
        /// "a token that was refreshed" is exactly the case it names), so it cannot await
        /// <see cref="GetTokenAsync"/>. This is the same value that call would return if a valid
        /// token happens to already be cached, without the fallback behavior.
        /// </remarks>
        string CachedAccessToken { get; }

        /// <summary>
        /// When the cached access token stops being handed out, or <see cref="DateTimeOffset.MinValue"/>
        /// if there is no usable token at all.
        /// </summary>
        /// <remarks>
        /// Exposed for one caller: whatever keeps the token alive in the background. Everything else
        /// should ask for a token and be told yes or no, rather than reasoning about clocks.
        /// <para>
        /// This is the moment <see cref="CachedAccessToken"/> starts returning "", not the moment
        /// WorkOS stops honouring the token — the store holds a safety margin back from the real
        /// expiry. A refresher must act before <em>this</em> time, not before the real one.
        /// </para>
        /// </remarks>
        DateTimeOffset AccessTokenExpiresAt { get; }

        /// <summary>
        /// Replaces the cached token using the stored refresh token. Never opens a browser.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="GetTokenAsync"/>, which returns the cached token untouched while
        /// one is still valid — correct for callers that just want a token, useless for keeping one
        /// alive, because by the time that call refreshes, the cached token has already gone empty
        /// and every request in the gap has failed. This one always goes to WorkOS.
        /// <para>
        /// Empty string on failure, like everything else here. A refresh that fails is not an
        /// exception; it means the player has to sign in again.
        /// </para>
        /// </remarks>
        Awaitable<string> RefreshAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>The service used when there is no SpacetimeDB connection to use a token on.</summary>
    public sealed class NullWorkOSAuthService : IWorkOSAuthService
    {
        public async Awaitable<string> GetTokenAsync(CancellationToken cancellationToken = default, bool interactive = false)
        {
            await Awaitable.NextFrameAsync(cancellationToken);
            return string.Empty;
        }

        public bool IsAvailable => false;

        public string PlayerEmail => string.Empty;

        public async Awaitable<string> SignInAsync(SignInRoute route, string email = null, CancellationToken cancellationToken = default)
        {
            await Awaitable.NextFrameAsync(cancellationToken);
            return string.Empty;
        }

        public string CachedAccessToken => string.Empty;

        public DateTimeOffset AccessTokenExpiresAt => DateTimeOffset.MinValue;

        public async Awaitable<string> RefreshAsync(CancellationToken cancellationToken = default)
        {
            await Awaitable.NextFrameAsync(cancellationToken);
            return string.Empty;
        }
    }
}
