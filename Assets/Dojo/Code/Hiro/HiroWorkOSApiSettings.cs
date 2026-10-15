using Dojo.Framework.Auth;
using Dojo.Framework.Net;

namespace Dojo.Hiro
{
    /// <summary>
    /// Where the Hiro API is and how to authenticate against it — the signed-in player's own
    /// WorkOS session, and nothing else.
    /// </summary>
    /// <remarks>
    /// A thin decorator over <see cref="HiroApiSettings"/> rather than a change to it: that class
    /// is a plain <c>[Serializable]</c> field on the root LifetimeScope, constructed by the
    /// inspector (or <c>new HiroApiSettings()</c>) rather than by the container, so it cannot take
    /// a constructor dependency on <see cref="IWorkOSAuthService"/>. This class can, and is what
    /// <c>RootLifetimeScope.RegisterServer</c> hands out as <see cref="IApiSettings"/> instead —
    /// every caller still depends on the interface and notices nothing.
    /// <para>
    /// <see cref="HiroApiSettings.ApiKey"/> (the dev-only <c>HIRO_API_KEY</c> environment
    /// variable) is deliberately not consulted here. The API key was always a workspace-wide,
    /// no-player-attached credential — a stand-in for the days before sign-in existed — and
    /// everything this settings object backs is now reachable with the player's own WorkOS
    /// session instead. <see cref="HiroApiSettings"/> itself is untouched and still resolvable on
    /// its own for whatever still wants it.
    /// </para>
    /// <para>
    /// <see cref="IWorkOSAuthService.CachedAccessToken"/> is a passive read — "never refreshes,
    /// never signs in, never touches the network" by its own contract — which is exactly right
    /// here: chatting is not the moment to pop a sign-in browser tab. A player who has not signed
    /// in this session, or who is running with WorkOS unconfigured, simply has no key yet; see
    /// <see cref="HasKey"/>.
    /// </para>
    /// <para>
    /// Read fresh on every call rather than cached: a player who signs in partway through a
    /// session, or whose token gets refreshed in the background, takes effect on the very next
    /// request rather than needing a restart.
    /// </para>
    /// </remarks>
    public sealed class HiroWorkOSApiSettings : IApiSettings
    {
        readonly HiroApiSettings inner;
        readonly IWorkOSAuthService workOS;

        public HiroWorkOSApiSettings(HiroApiSettings inner, IWorkOSAuthService workOS)
        {
            this.inner = inner;
            this.workOS = workOS;
        }

        public string BaseUrl => inner.BaseUrl;

        public string ApiKey => workOS?.CachedAccessToken ?? string.Empty;

        public bool HasKey => !string.IsNullOrEmpty(ApiKey);

        public int RequestTimeoutSeconds => inner.RequestTimeoutSeconds;

        public int StreamTimeoutSeconds => inner.StreamTimeoutSeconds;

        public bool LogRequests => inner.LogRequests;

        public string Label => inner.Label;

        public string MissingKeyAdvice => "Sign in with WorkOS to use this.";
    }
}
