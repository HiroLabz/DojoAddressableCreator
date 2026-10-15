using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Dojo.Framework.Auth
{
    /// <summary>
    /// Signs the player in through WorkOS AuthKit's hosted page, and hands back a token.
    /// </summary>
    /// <remarks>
    /// There is no WorkOS SDK for Unity, so this drives WorkOS Connect's OAuth Application
    /// endpoints directly: open the system browser to the hosted sign-in page
    /// (<c>Application.OpenURL</c>), receive the redirect back into the app, and exchange the
    /// resulting code for a token over plain HTTP. The same reasoning that keeps the generated
    /// SDK's Unity transport on <c>UnityWebRequest</c> instead of <c>HttpClient</c> applies here.
    /// <para>
    /// <b>This talks to a Connect OAuth Application, not WorkOS's User Management REST API</b> —
    /// an earlier version of this class used <c>/user_management/authorize</c> and
    /// <c>/user_management/authenticate</c> on <c>api.workos.com</c>, which worked for sign-in but
    /// produced tokens SpacetimeDB could never verify: SpacetimeDB fetches
    /// <c>{iss}/.well-known/openid-configuration</c> off of a token to find its signing key, and
    /// that host doesn't serve one (confirmed: 404). A Connect OAuth Application's endpoints live
    /// under the WorkOS environment's AuthKit domain instead
    /// (<see cref="WorkOSSettings.AuthKitDomain"/>), which does — confirmed by testing an issued
    /// token against this repo's own local SpacetimeDB instance and getting back a real identity.
    /// See <c>server/README.md</c> for the full investigation.
    /// </para>
    /// <para>
    /// The redirect itself arrives one of two ways depending on platform: a custom URI scheme
    /// (<c>Application.deepLinkActivated</c>) on a packaged mobile build, or
    /// <see cref="LoopbackRedirectListener"/> in the Editor and a Standalone build, where there is
    /// no installed app for the OS to hand a custom scheme back to. See
    /// <see cref="SignInInteractivelyAsync"/>.
    /// </para>
    /// <para>
    /// An ordinary object, not a component, for the same reason as the rest of this project's
    /// network-facing classes: nothing here reads a transform or runs per frame, and
    /// <c>Application.deepLinkActivated</c>
    /// is a static event a plain class can subscribe to directly.
    /// </para>
    /// </remarks>
    public sealed class WorkOSAuthService : IWorkOSAuthService
    {
        const string LogPrefix = "[WorkOS]";

        /// <summary>
        /// The scopes requested at sign-in. <c>offline_access</c> is what makes WorkOS include a
        /// <c>refresh_token</c> in the response — without it, only a short-lived access token
        /// comes back (confirmed: the AuthKit domain's discovery document lists
        /// <c>offline_access</c> as one of its <c>scopes_supported</c>, alongside the standard
        /// OIDC ones).
        /// </summary>
        const string Scope = "openid profile email offline_access";

        readonly WorkOSSettings settings;

        /// <summary>
        /// The attempt in progress, if any.
        /// </summary>
        /// <remarks>
        /// Several <c>Dojo.Spacetime</c> publishers each call <c>SpacetimeConnection.ConnectAsync</c>
        /// independently; without this, the first few to run before any of them finishes would
        /// each open their own browser tab for the same sign-in. A second caller instead awaits
        /// the first attempt's outcome.
        /// </remarks>
        AwaitableCompletionSource<string> inFlight;

        public WorkOSAuthService(WorkOSSettings settings)
        {
            this.settings = settings ?? new WorkOSSettings();
        }

        public bool IsAvailable => settings.IsConfigured;

        public string PlayerEmail => WorkOSTokenStore.Email;

        public Awaitable<string> SignInAsync(SignInRoute route, string email = null, CancellationToken cancellationToken = default)
            => GetTokenCoreAsync(cancellationToken, true, route, email);

        public string CachedAccessToken =>
            WorkOSTokenStore.HasValidAccessToken ? WorkOSTokenStore.AccessToken : string.Empty;

        public DateTimeOffset AccessTokenExpiresAt => WorkOSTokenStore.ExpiresAt;

        /// <summary>
        /// Trades the stored refresh token for a new access token, whatever the cached one says.
        /// </summary>
        /// <remarks>
        /// Deliberately does not consult <c>HasValidAccessToken</c> first, which is the one thing
        /// separating this from <see cref="GetTokenAsync"/>: a caller keeping the session alive has
        /// to be able to replace a token that is still valid but about to stop being.
        /// </remarks>
        public async Awaitable<string> RefreshAsync(CancellationToken cancellationToken = default)
        {
            if (!settings.IsConfigured)
            {
                return string.Empty;
            }

            var refreshToken = WorkOSTokenStore.RefreshToken;

            if (string.IsNullOrEmpty(refreshToken))
            {
                return string.Empty;
            }

            var refreshed = await ExchangeAsync(
                BuildRefreshBody(settings.ClientId, refreshToken), cancellationToken);

            if (refreshed != null)
            {
                return refreshed.access_token;
            }

            Debug.Log($"{LogPrefix} stored refresh token was rejected on a background refresh.");

            return string.Empty;
        }

        public Awaitable<string> GetTokenAsync(CancellationToken cancellationToken = default, bool interactive = false)
            => GetTokenCoreAsync(cancellationToken, interactive, SignInRoute.Hosted, null);

        async Awaitable<string> GetTokenCoreAsync(CancellationToken cancellationToken, bool interactive, SignInRoute route, string email)
        {
            if (!settings.IsConfigured)
            {
                Debug.LogWarning($"{LogPrefix} not configured (client id or AuthKit domain missing); "
                    + "carrying on without a token.");
                return string.Empty;
            }

            if (WorkOSTokenStore.HasValidAccessToken)
            {
                return WorkOSTokenStore.AccessToken;
            }

            // Non-interactive callers never open a browser, so there is nothing to coalesce — each
            // just does its own quick refresh-or-nothing check. Coalescing only matters for the
            // interactive path below, where a second caller must not open a second browser tab.
            if (!interactive)
            {
                return await AcquireTokenAsync(cancellationToken, false, route, email);
            }

            if (inFlight != null)
            {
                try
                {
                    return await inFlight.Awaitable;
                }
                catch (OperationCanceledException)
                {
                    return string.Empty;
                }
            }

            inFlight = new AwaitableCompletionSource<string>();

            try
            {
                var token = await AcquireTokenAsync(cancellationToken, true, route, email);
                inFlight.TrySetResult(token);
                return token;
            }
            catch (OperationCanceledException)
            {
                inFlight.TrySetCanceled();
                return string.Empty;
            }
            finally
            {
                inFlight = null;
            }
        }

        async Awaitable<string> AcquireTokenAsync(CancellationToken cancellationToken, bool interactive, SignInRoute route, string email)
        {
            // Re-checked: this attempt may have been queued behind one that just finished.
            if (WorkOSTokenStore.HasValidAccessToken)
            {
                return WorkOSTokenStore.AccessToken;
            }

            var refreshToken = WorkOSTokenStore.RefreshToken;

            if (!string.IsNullOrEmpty(refreshToken))
            {
                var refreshed = await ExchangeAsync(
                    BuildRefreshBody(settings.ClientId, refreshToken), cancellationToken);

                if (refreshed != null)
                {
                    return refreshed.access_token;
                }

                Debug.Log($"{LogPrefix} stored refresh token was rejected.");
            }

            return interactive ? await SignInInteractivelyAsync(route, email, cancellationToken) : string.Empty;
        }

        /// <summary>
        /// Opens the browser for sign-in and waits for whichever redirect path this platform uses.
        /// </summary>
        /// <remarks>
        /// The Editor and Standalone builds have no installed app for the OS to hand
        /// <c>dojo://auth-callback</c> back to, so <see cref="LoopbackRedirectListener"/> stands
        /// in for it there — a temporary local HTTP listener instead of a custom URI scheme,
        /// exactly the split RFC 8252 §7.3 recommends between desktop and mobile native apps.
        /// Each app the player is registered against needs both redirect URIs added in the WorkOS
        /// dashboard: the custom scheme for mobile builds, and
        /// <c>http://127.0.0.1:&lt;WorkOSSettings.DesktopRedirectPort&gt;/callback</c> for
        /// everything else.
        /// </remarks>
        async Awaitable<string> SignInInteractivelyAsync(SignInRoute route, string email, CancellationToken cancellationToken)
        {
            var pkce = PkceChallenge.Create();
            var state = PkceChallenge.NewState();
            var useLoopback = LoopbackRedirectListener.IsSupported;
            var redirectUri = useLoopback
                ? $"http://127.0.0.1:{settings.DesktopRedirectPort}/callback"
                : settings.RedirectUri;
            var authorizeUrl = WorkOSAuthorizeUrl.Build(
                AuthorizeEndpoint, settings.ClientId, redirectUri, pkce.Challenge, state, Scope, route, email);

            string code = useLoopback
                ? await SignInViaLoopbackAsync(authorizeUrl, state, cancellationToken)
                : await SignInViaDeepLinkAsync(authorizeUrl, state, cancellationToken);

            if (string.IsNullOrEmpty(code))
            {
                return string.Empty;
            }

            var exchanged = await ExchangeAsync(
                BuildAuthorizationCodeBody(settings.ClientId, code, pkce.Verifier, redirectUri), cancellationToken);

            return exchanged?.access_token ?? string.Empty;
        }

        async Awaitable<string> SignInViaLoopbackAsync(string authorizeUrl, string state, CancellationToken cancellationToken)
        {
            // The address in full: which page the browser was asked for is the first question when
            // a sign-in does not look the way it should. Nothing in it is secret - the PKCE
            // challenge is public by design, and the state is good for this one attempt only.
            Debug.Log($"{LogPrefix} opening the system browser for sign-in "
                + $"(loopback redirect on port {settings.DesktopRedirectPort}):\n{authorizeUrl}");

            // Listening starts before the browser opens, so the redirect can never arrive first.
            var listen = LoopbackRedirectListener.ListenOnceAsync(
                settings.DesktopRedirectPort, settings.SignInTimeoutSeconds, cancellationToken);

            Application.OpenURL(authorizeUrl);

            string requestLine;

            try
            {
                requestLine = await listen;
            }
            catch (OperationCanceledException)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(requestLine))
            {
                return string.Empty;
            }

            // "GET /callback?code=...&state=... HTTP/1.1" — only the middle token is wanted.
            var parts = requestLine.Split(' ');
            var pathAndQuery = parts.Length >= 2 ? parts[1] : string.Empty;

            return ResolveCode(ParseQuery("http://127.0.0.1" + pathAndQuery), state);
        }

        async Awaitable<string> SignInViaDeepLinkAsync(string authorizeUrl, string state, CancellationToken cancellationToken)
        {
            var callback = new AwaitableCompletionSource<string>();

            void OnDeepLink(string url) => callback.TrySetResult(ResolveCode(ParseQuery(url), state));

            Application.deepLinkActivated += OnDeepLink;

            try
            {
                Debug.Log($"{LogPrefix} opening the system browser for sign-in.");
                Application.OpenURL(authorizeUrl);

                _ = TimeOutAsync(callback, settings.SignInTimeoutSeconds, cancellationToken);

                try
                {
                    return await callback.Awaitable;
                }
                catch (OperationCanceledException)
                {
                    return string.Empty;
                }
            }
            finally
            {
                Application.deepLinkActivated -= OnDeepLink;
            }
        }

        /// <summary>Pulls the authorization code out of a redirect's query, or "" if it can't be trusted.</summary>
        static string ResolveCode(Dictionary<string, string> query, string expectedState)
        {
            if (!query.TryGetValue("state", out var state) || state != expectedState)
            {
                // Either a forged callback or a stale one from an earlier, already-abandoned
                // attempt. Never treated as this attempt's answer.
                Debug.LogWarning($"{LogPrefix} redirect carried an unexpected or missing state; ignored.");
                return string.Empty;
            }

            if (query.TryGetValue("error", out var error))
            {
                Debug.LogWarning($"{LogPrefix} sign-in was denied or failed: {error}");
                return string.Empty;
            }

            return query.TryGetValue("code", out var code) ? code : string.Empty;
        }

        async Awaitable TimeOutAsync(
            AwaitableCompletionSource<string> pending, float seconds, CancellationToken cancellationToken)
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(seconds, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                pending.TrySetCanceled();
                return;
            }

            if (pending.TrySetResult(string.Empty))
            {
                Debug.LogWarning($"{LogPrefix} sign-in did not complete within {seconds:0}s; "
                    + "carrying on without it.");
            }
        }

        string AuthorizeEndpoint => $"https://{settings.AuthKitDomain}/oauth2/authorize";

        string TokenEndpoint => $"https://{settings.AuthKitDomain}/oauth2/token";

        /// <summary>
        /// The authorization-code exchange body. Standard RFC 6749 form encoding, and no
        /// <c>client_secret</c>: the discovery document lists <c>"none"</c> among
        /// <c>token_endpoint_auth_methods_supported</c>, which is exactly the public-client/PKCE
        /// case this Connect application was created as.
        /// </summary>
        static string BuildAuthorizationCodeBody(string clientId, string code, string codeVerifier, string redirectUri) =>
            string.Join("&", new[]
            {
                "grant_type=authorization_code",
                "client_id=" + Uri.EscapeDataString(clientId),
                "code=" + Uri.EscapeDataString(code),
                "code_verifier=" + Uri.EscapeDataString(codeVerifier),
                "redirect_uri=" + Uri.EscapeDataString(redirectUri),
            });

        /// <summary>The refresh-token grant body, again with no <c>client_secret</c>.</summary>
        static string BuildRefreshBody(string clientId, string refreshToken) =>
            string.Join("&", new[]
            {
                "grant_type=refresh_token",
                "client_id=" + Uri.EscapeDataString(clientId),
                "refresh_token=" + Uri.EscapeDataString(refreshToken),
            });

        /// <summary>
        /// Posts a token request and, on success, saves and returns the parsed response.
        /// </summary>
        /// <remarks>
        /// This is a standard OIDC token endpoint (confirmed via its own discovery document and
        /// by testing a real exchange), unlike the User Management REST API this class used to
        /// target: plain form encoding, and a response with real <c>access_token</c>,
        /// <c>refresh_token</c> (since <see cref="Scope"/> requests <c>offline_access</c>),
        /// <c>id_token</c> and <c>expires_in</c> fields — no JWT-decoding workaround needed for
        /// the expiry.
        /// </remarks>
        async Awaitable<TokenResponse> ExchangeAsync(string formBody, CancellationToken cancellationToken)
        {
            using (var request = new UnityWebRequest(TokenEndpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(formBody));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
                request.SetRequestHeader("Accept", "application/json");

                try
                {
                    await Awaitable.FromAsyncOperation(request.SendWebRequest(), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    request.Abort();
                    throw;
                }

                if (request.result != UnityWebRequest.Result.Success || request.responseCode >= 400)
                {
                    Debug.LogWarning($"{LogPrefix} token request failed: {request.responseCode} "
                        + $"{request.error} {request.downloadHandler.text}");
                    return null;
                }

                TokenResponse response;

                try
                {
                    response = JsonUtility.FromJson<TokenResponse>(request.downloadHandler.text);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"{LogPrefix} could not parse the token response: {exception.Message}");
                    return null;
                }

                if (response == null || string.IsNullOrEmpty(response.access_token))
                {
                    Debug.LogWarning($"{LogPrefix} token response had no access_token.");
                    return null;
                }

                // A short, conservative fallback for the rare case expires_in is missing or zero —
                // treating it as already-nearly-expired is safer than assuming a long lifetime,
                // since the caller just re-authenticates rather than trusting a stale token.
                var expiresAt = DateTimeOffset.UtcNow.AddSeconds(response.expires_in > 0 ? response.expires_in : 60);

                WorkOSTokenStore.Save(response.access_token, response.refresh_token, expiresAt);
                WorkOSTokenStore.SaveEmail(WorkOSTokenClaims.EmailOf(response.id_token));

                return response;
            }
        }

        static Dictionary<string, string> ParseQuery(string url)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(url))
            {
                return result;
            }

            Uri uri;

            try
            {
                uri = new Uri(url);
            }
            catch (UriFormatException)
            {
                return result;
            }

            var query = uri.Query.TrimStart('?');

            if (string.IsNullOrEmpty(query))
            {
                return result;
            }

            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                var separator = pair.IndexOf('=');
                var key = separator < 0 ? pair : pair.Substring(0, separator);
                var value = separator < 0 ? string.Empty : Uri.UnescapeDataString(pair.Substring(separator + 1));
                result[Uri.UnescapeDataString(key)] = value;
            }

            return result;
        }

        /// <summary>
        /// The fields this class needs out of the token endpoint's response. It also carries
        /// <c>token_type</c> — <c>JsonUtility</c> silently ignores whatever a class doesn't
        /// declare, so that is left out rather than modeled and unused. <c>id_token</c> is read
        /// only for the player's email, which the Lobby shows.
        /// </summary>
        [Serializable]
        sealed class TokenResponse
        {
            public string access_token;
            public string refresh_token;
            public string id_token;
            public int expires_in;
        }
    }
}
