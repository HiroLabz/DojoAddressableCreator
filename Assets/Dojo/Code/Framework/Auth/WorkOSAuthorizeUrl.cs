using System;
using System.Collections.Generic;

namespace Dojo.Framework.Auth
{
    /// <summary>The address the sign-in browser opens: WorkOS's authorize endpoint and its query.</summary>
    /// <remarks>
    /// Out of <see cref="WorkOSAuthService"/> so the query can be checked without a browser. A route
    /// only adds to the standard PKCE request, never changes it, so a route WorkOS ignores still
    /// lands on the ordinary sign-in page rather than failing.
    /// <para>
    /// Every request carries <c>prompt=login</c>. The browser keeps its own WorkOS session, and
    /// without this a browser still signed in hands the game a sign-in straight away, never showing
    /// the page or its email, Google and Apple choices. Checked in Chrome on 2026-09-28: with it the
    /// full sign-in page shows even while signed in. <c>max_age=0</c>, which WorkOS documents for
    /// this, was tried first and does nothing on this endpoint. The browser only opens when the
    /// player has pressed a button asking to sign in, so showing the page is what they asked for.
    /// </para>
    /// <para>
    /// Google is also asked for its account chooser - <c>prompt=select_account</c>, passed through
    /// WorkOS's <c>provider_query_params</c> in the <c>key[subKey]</c> form WorkOS's own SDK writes -
    /// so a browser signed in to Google does not pick the account without asking. Whether this
    /// endpoint passes it on is unconfirmed; it is harmless if not. Not on the Apple route: that
    /// one never reaches Google, and Apple has no such setting.
    /// </para>
    /// </remarks>
    public static class WorkOSAuthorizeUrl
    {
        public static string Build(string endpoint, string clientId, string redirectUri, string codeChallenge,
            string state, string scope, SignInRoute route, string email)
        {
            var query = new List<string>
            {
                "response_type=code",
                "client_id=" + Uri.EscapeDataString(clientId),
                "redirect_uri=" + Uri.EscapeDataString(redirectUri),
                "code_challenge=" + Uri.EscapeDataString(codeChallenge),
                "code_challenge_method=S256",
                "state=" + Uri.EscapeDataString(state),
                "scope=" + Uri.EscapeDataString(scope),
                "prompt=login",
            };

            if (route != SignInRoute.Apple)
            {
                query.Add(Uri.EscapeDataString("provider_query_params[prompt]") + "=select_account");
            }

            var hint = string.IsNullOrWhiteSpace(email) ? null : "login_hint=" + Uri.EscapeDataString(email.Trim());

            switch (route)
            {
                case SignInRoute.Email:
                    if (hint != null) query.Add(hint);
                    break;

                case SignInRoute.SignUp:
                    query.Add("screen_hint=sign-up");
                    if (hint != null) query.Add(hint);
                    break;

                case SignInRoute.Google:
                    query.Add("provider=GoogleOAuth");
                    break;

                case SignInRoute.Apple:
                    query.Add("provider=AppleOAuth");
                    break;
            }

            return endpoint + "?" + string.Join("&", query);
        }
    }
}
