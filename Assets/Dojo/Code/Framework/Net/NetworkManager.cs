using System.Collections;
using System.Threading;
using Dojo.Framework.Events;
using UnityEngine;
using UnityEngine.Networking;
using VContainer;

namespace Dojo.Framework.Net
{
    /// <summary>
    /// Watches whether the game can reach its server, and announces every change on the event hub
    /// as a <see cref="ConnectivityChanged"/>.
    /// </summary>
    /// <remarks>
    /// <b>The first thing alive.</b> It makes itself before the first scene loads, so it is there
    /// before the Bootstrapper, the root LifetimeScope or anything they start, and it survives every
    /// scene change after that. Reach it as <see cref="Instance"/>, or have it injected: the root
    /// scope registers this same instance.
    /// <para>
    /// <b>"Connected" means the server answered.</b> Unity's own
    /// <c>Application.internetReachability</c> only says whether a network adapter is up - it says
    /// yes behind a dead router or on hotel Wi-Fi before its sign-in page - so it is used only as
    /// the quick "no network at all" test. The real test is a request to the Hiro API, the server
    /// the game actually depends on, which is also one a WebGL build is allowed to call once that
    /// server permits the game's origin. Any answer counts, an error page included: what is being
    /// asked is whether the server can be reached, not whether it liked the request.
    /// </para>
    /// <para>
    /// <b>When it checks</b> is <see cref="ConnectivityMonitor"/>'s business: every 30 seconds
    /// while online, twice before calling a drop, quickly and then less often while offline. On
    /// top of that, straight away whenever the adapter changes, the app comes back into focus, or
    /// somebody calls <see cref="CheckNow"/> - after a request of their own failed, say.
    /// </para>
    /// <para>
    /// It needs the hub to announce anything and the API settings to know where to knock, and
    /// both live in the root container, which is built a moment after this wakes. Until they
    /// arrive it only watches the adapter; the moment they do, it checks, and announces the result.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-10000)]
    public sealed class NetworkManager : MonoBehaviour
    {
        const string LogPrefix = "[Network]";

        /// <summary>Seconds a single check may take before it counts as no answer.</summary>
        const int ProbeTimeoutSeconds = 5;

        /// <summary>How often the network adapter is looked at. Free: no request is made.</summary>
        const float AdapterPollSeconds = 1f;

        readonly ConnectivityMonitor monitor = new ConnectivityMonitor();

        IEventHub hub;
        string probeUrl;

        // Set once the root scope has handed over the hub and the address. Until then nothing but
        // the adapter is watched: a guess of "online" from the adapter alone would be announced the
        // moment the hub arrived, and could be wrong.
        bool constructed;

        float nextCheckAt;
        float nextAdapterPollAt;
        NetworkReachability adapter;
        bool probing;
        string lastError;

        // Every verdict counts one, so a caller can wait for "the answer after the one I asked for"
        // rather than whatever answer happened to be current.
        int answers;

        /// <summary>The one instance, alive from before the first scene loads.</summary>
        public static NetworkManager Instance { get; private set; }

        /// <summary>What the connection is now.</summary>
        public ConnectionState State => monitor.State;

        /// <summary>Shorthand for the question nearly every caller asks.</summary>
        public bool IsOnline => monitor.State == ConnectionState.Online;

        /// <summary>The address it checks, or null until the root scope has supplied one.</summary>
        public string ProbeUrl => probeUrl;

        /// <summary>
        /// Cleared at the very start of every play session, so the instance from the last session
        /// is never taken for this one when domain reload is switched off.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ForgetLastSession()
        {
            Instance = null;
        }

        /// <summary>Made before the first scene loads - the earliest point a GameObject can be.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void WakeFirst()
        {
            EnsureExists();
        }

        /// <summary>The instance, made now if it does not exist yet.</summary>
        public static NetworkManager EnsureExists()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var host = new GameObject(nameof(NetworkManager));
            return host.AddComponent<NetworkManager>();
        }

        /// <summary>Hands over the hub to announce on and the server to check.</summary>
        /// <remarks>
        /// Checks straight away rather than waiting for the next scheduled check, which could be
        /// half a minute off, and announces what it knows so far so the hub holds a state from the
        /// start.
        /// </remarks>
        [Inject]
        public void Construct(IEventHub hub, IApiSettings api)
        {
            this.hub = hub;
            probeUrl = api != null && !string.IsNullOrWhiteSpace(api.BaseUrl) ? api.BaseUrl : null;
            constructed = true;

            if (probeUrl == null)
            {
                Debug.LogWarning(LogPrefix + " No server address was supplied, so only the network "
                    + "adapter is watched and \"online\" means no more than Wi-Fi or a cable is up.", this);
            }
            else
            {
                Debug.Log(LogPrefix + " Checking " + probeUrl + " now, then every "
                    + ConnectivityMonitor.OnlineInterval + "s while online.", this);
            }

            if (monitor.State != ConnectionState.Unknown)
            {
                Announce(monitor.State);
            }

            CheckNow();
        }

        /// <summary>Checks again at the next opportunity, whatever the schedule says.</summary>
        public void CheckNow()
        {
            nextCheckAt = 0f;
        }

        /// <summary>
        /// Waits until the first check has answered, and returns what it said. Immediate once any
        /// check has.
        /// </summary>
        /// <remarks>
        /// What startup waits on before deciding whether to open a sign-in browser. Quick either
        /// way: with no network at all the adapter answers at once, and one failed check is enough
        /// at startup - see <see cref="ConnectivityMonitor"/>.
        /// </remarks>
        public async Awaitable<ConnectionState> FirstAnswerAsync(CancellationToken token)
        {
            while (monitor.State == ConnectionState.Unknown)
            {
                await Awaitable.NextFrameAsync(token);
            }

            return monitor.State;
        }

        /// <summary>
        /// Checks now and waits for that check's answer - not whatever was already known.
        /// </summary>
        /// <remarks>
        /// For a Retry button: the player has done something about their network and wants to know
        /// whether it worked, so an answer from before they pressed it is no answer.
        /// </remarks>
        public async Awaitable<ConnectionState> CheckNowAsync(CancellationToken token)
        {
            // No adapter: nothing to send, and nothing would ever be sent to wait for.
            adapter = Application.internetReachability;
            if (adapter == NetworkReachability.NotReachable)
            {
                Apply(monitor.AdapterLost(Time.unscaledTime), "no network adapter is up");
                return monitor.State;
            }

            var before = answers;
            CheckNow();

            while (answers == before)
            {
                await Awaitable.NextFrameAsync(token);
            }

            return monitor.State;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // One is enough. A copy authored into a scene gives way to the one made at start-up.
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            adapter = Application.internetReachability;

            if (adapter == NetworkReachability.NotReachable)
            {
                Apply(monitor.AdapterLost(Time.unscaledTime), "no network adapter is up");
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused)
            {
                CheckNow();
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                CheckNow();
            }
        }

        void Update()
        {
            var now = Time.unscaledTime;

            if (now >= nextAdapterPollAt)
            {
                nextAdapterPollAt = now + AdapterPollSeconds;
                WatchAdapter(now);
            }

            if (!constructed || probing || now < nextCheckAt || adapter == NetworkReachability.NotReachable)
            {
                return;
            }

            if (probeUrl == null)
            {
                // Told there is nothing to knock on: an adapter that is up is the best that can be said.
                Apply(monitor.Report(true, now), "a network adapter is up; no server to check");
                return;
            }

            StartCoroutine(Probe());
        }

        /// <summary>Reacts the moment Wi-Fi or a cable comes or goes, without sending anything.</summary>
        void WatchAdapter(float now)
        {
            var current = Application.internetReachability;

            if (current == adapter)
            {
                return;
            }

            adapter = current;

            if (current == NetworkReachability.NotReachable)
            {
                Apply(monitor.AdapterLost(now), "the network adapter went down");
            }
            else
            {
                // Back, or switched between Wi-Fi and mobile data: find out properly.
                CheckNow();
            }
        }

        IEnumerator Probe()
        {
            probing = true;

            bool reached;
            string error;

            using (var request = UnityWebRequest.Head(probeUrl))
            {
                request.timeout = ProbeTimeoutSeconds;

                yield return request.SendWebRequest();

                // A status code of any kind is an answer. Only a failure to get one - no route, no
                // name, no reply in time - means the server cannot be reached.
                reached = request.result == UnityWebRequest.Result.Success
                          || request.result == UnityWebRequest.Result.ProtocolError;
                error = reached ? null : request.error;
            }

            probing = false;

            Apply(monitor.Report(reached, Time.unscaledTime), error);
        }

        void Apply(ConnectivityMonitor.Verdict verdict, string why)
        {
            answers++;
            nextCheckAt = verdict.NextCheckAt;

            if (!string.IsNullOrEmpty(why))
            {
                lastError = why;
            }

            if (!verdict.Changed)
            {
                return;
            }

            if (verdict.Current == ConnectionState.Online)
            {
                Debug.Log(LogPrefix + " Online" + (probeUrl != null ? " - " + probeUrl + " answered." : "."), this);
            }
            else
            {
                Debug.LogWarning(LogPrefix + " Offline - " + (lastError ?? "no answer") + ".", this);
            }

            Announce(verdict.Previous);
        }

        /// <summary>
        /// Sticky, so anything that subscribes later - a screen opened long after the change - is
        /// handed the state at once instead of waiting for a change that may never come.
        /// </summary>
        void Announce(ConnectionState previous)
        {
            if (hub == null)
            {
                return;   // announced as soon as the hub arrives; see Construct
            }

            hub.PublishSticky(new ConnectivityChanged(monitor.State, previous));
        }
    }
}
