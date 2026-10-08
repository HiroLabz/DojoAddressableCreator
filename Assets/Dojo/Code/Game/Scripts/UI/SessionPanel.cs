using Dojo.Framework.Auth;
using Dojo.Framework.Content;
using Dojo.Framework.Net;
using Dojo.Framework.Session;
using Dojo.Framework.Startup;
using Dojo.Game.Systems;
using TMPro;
using UnityEngine;
using VContainer;

namespace Dojo.Game
{
    /// <summary>
    /// The main menu's SESSION panel - realtime, identity, content and build - and the build badge
    /// in the top bar, filled from what is actually true right now.
    /// </summary>
    /// <remarks>
    /// Re-read on every readiness and entitlement change rather than once: all four rows describe
    /// things that arrive during startup, and a panel filled before the connection answered would
    /// be a snapshot of "nothing has happened yet" left on screen for the rest of the session.
    /// <para>
    /// Every row degrades to a dash rather than to a guess. "—" reads as not known, which is the
    /// truth before a preprocess has finished; a plausible-looking default would be a lie nobody
    /// could spot.
    /// </para>
    /// <para>
    /// Injected by <c>LobbyLifetimeScope</c>. Uninjected - the Lobby opened on its own - every
    /// row still fills, from what can be known without the app behind it.
    /// </para>
    /// </remarks>
    public sealed class SessionPanel : MonoBehaviour
    {
        [Header("Rows")]
        [Tooltip("Realtime backend state, e.g. 'spacetime · live'.")]
        [SerializeField] TextMeshProUGUI realtimeValue;

        [Tooltip("Who the player is signed in as.")]
        [SerializeField] TextMeshProUGUI identityValue;

        [Tooltip("How much content is resident, e.g. '2 packs resident'.")]
        [SerializeField] TextMeshProUGUI contentValue;

        [Tooltip("Build string shown in the panel.")]
        [SerializeField] TextMeshProUGUI buildValue;

        [Header("Build badge")]
        [Tooltip("Top-right build badge. Shows the same build string as the panel.")]
        [SerializeField] TextMeshProUGUI buildBadgeLabel;

        [Tooltip("The home screen's footer, bottom left. Shows the same build string again.")]
        [SerializeField] TextMeshProUGUI footerBuild;

        [Header("Build")]
        [Tooltip("The branch or build name shown after the version, e.g. 'dev_prototype_2'. The " +
                 "version itself comes from Player Settings and is not repeated here.")]
        [SerializeField] string buildTag = "dev_prototype_2";

        IAppReadiness readiness;
        IEntitlements entitlements;
        ISessionService session;
        IContentService content;
        IWorkOSAuthService workOS;

        /// <summary>
        /// Whether a realtime backend is configured at all.
        /// </summary>
        /// <remarks>
        /// The settings object rather than <c>ISpacetimeConnection</c>, which is registered only
        /// when the backend is switched on — taking it here would make this panel unresolvable in a
        /// build with the backend off, which is the configuration it most needs to keep working.
        /// </remarks>
        SpacetimeSettings spacetime;

        [Inject]
        public void Construct(
            IAppReadiness readiness,
            IEntitlements entitlements,
            ISessionService session,
            IContentService content,
            IWorkOSAuthService workOS,
            SpacetimeSettings spacetime)
        {
            this.readiness = readiness;
            this.entitlements = entitlements;
            this.session = session;
            this.content = content;
            this.workOS = workOS;
            this.spacetime = spacetime;
        }

        void Start()
        {
            if (readiness != null)
            {
                readiness.Changed += Apply;
            }

            if (entitlements != null)
            {
                entitlements.Changed += OnEntitlementsChanged;
            }

            // Applied once on top of subscribing: a preprocess that finished before this panel woke
            // has already raised its only Changed.
            Apply();
        }

        void OnDestroy()
        {
            if (readiness != null)
            {
                readiness.Changed -= Apply;
            }

            if (entitlements != null)
            {
                entitlements.Changed -= OnEntitlementsChanged;
            }
        }

        /// <summary>A pack arriving changes what is resident, which is one of the four rows.</summary>
        void OnEntitlementsChanged(EntitlementsChanged change) => Apply();

        void Apply()
        {
            var build = Build();

            SetText(realtimeValue, Realtime());
            SetText(identityValue, Identity());
            SetText(contentValue, ContentSummary());
            SetText(buildValue, build);
            SetText(buildBadgeLabel, build);
            SetText(footerBuild, build);
        }

        /// <remarks>
        /// Two separate facts: whether a backend is configured, and whether the connection that
        /// gates the Lobby got through. A build with the backend switched off is not a failure and
        /// must not read like one.
        /// </remarks>
        string Realtime()
        {
            if (spacetime == null || !spacetime.Enabled)
            {
                return "local · offline";
            }

            if (readiness == null)
            {
                return "spacetime · —";
            }

            switch (readiness.StateOf(AppStartup.ConnectionPreprocess))
            {
                case PreprocessState.Succeeded:
                    return "spacetime · live";

                case PreprocessState.Failed:
                    return "spacetime · offline";

                default:
                    return "spacetime · connecting";
            }
        }

        /// <remarks>
        /// The WorkOS email first: that is who the player signed in as. The session's player id is
        /// the fallback for a build with sign-in switched off.
        /// </remarks>
        string Identity()
        {
            var email = workOS == null ? string.Empty : workOS.PlayerEmail;

            if (!string.IsNullOrEmpty(email))
            {
                return email;
            }

            SessionResult current = session == null ? null : session.Current;

            return current == null || string.IsNullOrEmpty(current.PlayerId)
                ? "—"
                : current.PlayerId;
        }

        /// <remarks>
        /// Resident, not entitled — the two differ while a download is still running, and this row
        /// says what is resident. Listing what the player is owed rather than what actually arrived
        /// would make a failed preload invisible on the one screen built to show session state.
        /// <para>
        /// By name - "Default, Strawberry" - since the row became PACKS and took over from the pack
        /// chips that used to sit under the menu.
        /// </para>
        /// </remarks>
        string ContentSummary()
        {
            if (content == null || content.LoadedKeys == null || content.LoadedKeys.Count == 0)
            {
                return "—";
            }

            var names = new System.Collections.Generic.List<string>();

            foreach (var key in content.LoadedKeys)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    names.Add(char.ToUpperInvariant(key[0]) + key.Substring(1));
                }
            }

            return names.Count == 0 ? "—" : string.Join(", ", names);
        }

        string Build()
        {
            string version = Application.version;

            if (string.IsNullOrEmpty(version))
            {
                version = "—";
            }

            return string.IsNullOrEmpty(buildTag) ? version : version + " · " + buildTag;
        }

        static void SetText(TextMeshProUGUI label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }
    }
}
