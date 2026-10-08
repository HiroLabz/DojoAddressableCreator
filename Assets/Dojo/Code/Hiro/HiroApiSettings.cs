using System;
using Dojo.Framework.Net;
using UnityEngine;

namespace Dojo.Hiro
{
    /// <summary>
    /// Where the Hiro API is and how to authenticate against it.
    /// </summary>
    /// <remarks>
    /// A plain serialisable class exposed on the root LifetimeScope, the same shape
    /// <c>WorldSettings</c> takes and for the same reason: the thing that reads it is an ordinary
    /// object rather than a component, so the values are authored in the inspector while nothing
    /// about the reader needs a GameObject.
    /// <para>
    /// Implements <see cref="IApiSettings"/>, which is what the transport down in
    /// <c>Dojo.Framework.Net</c> actually takes. Everything Hiro-specific — the default URL, the
    /// name of the environment variable, the advice printed when the key is missing — stays here
    /// in the vendor layer, and the transport learns none of it.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class HiroApiSettings : IApiSettings
    {
        [Tooltip("Base URL of the API, no trailing slash. Paths are appended to it.")]
        [SerializeField] string baseUrl = "https://sea-turtle-app-e4f5j.ondigitalocean.app";

        [Tooltip("Name of the environment variable holding the API key. The key itself is " +
                 "deliberately NOT a field here — see the remarks on ApiKey.")]
        [SerializeField] string apiKeyVariable = "HIRO_API_KEY";

        [Tooltip("Seconds a plain request may take before it is abandoned.")]
        [SerializeField] int requestTimeoutSeconds = 30;

        [Tooltip("Seconds a streamed answer may take. Generous: a real reply took eight seconds " +
                 "of thinking before the first token arrived.")]
        [SerializeField] int streamTimeoutSeconds = 120;

        [Tooltip("Log every request and its status. Off by default; a chat that works is not " +
                 "something to narrate sixty times a session.")]
        [SerializeField] bool logRequests;

        [Tooltip("Where REGISTER NOW sends a player whose account is not linked to Hiro.")]
        [SerializeField] string registrationUrl = "https://hirolabz.com";

        /// <summary>Base URL with any trailing slash removed, so path joining is unambiguous.</summary>
        public string BaseUrl => (baseUrl ?? string.Empty).TrimEnd('/');

        /// <summary>Name of the environment variable the key is read from.</summary>
        public string ApiKeyVariable => apiKeyVariable;

        public int RequestTimeoutSeconds => Mathf.Max(1, requestTimeoutSeconds);

        public int StreamTimeoutSeconds => Mathf.Max(1, streamTimeoutSeconds);

        public bool LogRequests => logRequests;

        /// <summary>Where REGISTER NOW sends a player whose account is not linked to Hiro.</summary>
        public string RegistrationUrl => (registrationUrl ?? string.Empty).Trim();

        /// <summary>
        /// The API key, read from the environment every time rather than cached or serialized.
        /// </summary>
        /// <remarks>
        /// <b>Deliberately not a serialized field.</b> A key on a component is written into the
        /// scene or prefab asset, and from there into git — and a key compiled into a build is
        /// readable by anyone who downloads the game, with nothing more than <c>strings</c>. An
        /// environment variable keeps it on the developer's machine and out of the repository.
        /// <para>
        /// Which also means it is <em>absent</em> in a shipped build, because an environment
        /// variable belongs to the machine that set it and a player never set one. That is not an
        /// oversight to work around by baking the key in: it is the point at which the call has to
        /// go through a server of your own that holds the key, or the player has to supply theirs.
        /// Until then this is a development-only path, and <see cref="HasKey"/> says so plainly.
        /// </para>
        /// <para>
        /// Read fresh each time so a key rotated mid-session takes effect without a restart. It is
        /// an environment lookup, not a network call.
        /// </para>
        /// </remarks>
        public string ApiKey
        {
            get
            {
                var variable = string.IsNullOrEmpty(apiKeyVariable) ? "HIRO_API_KEY" : apiKeyVariable;

                // User scope explicitly as well as the process block: a variable set through the
                // Windows GUI after the editor started is in the registry but not in this
                // process's inherited environment, and the process lookup alone would miss it.
                var value = Environment.GetEnvironmentVariable(variable);

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                if (string.IsNullOrEmpty(value))
                {
                    value = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User);
                }
#endif

                return value;
            }
        }

        /// <summary>True when there is a key to authenticate with.</summary>
        public bool HasKey => !string.IsNullOrEmpty(ApiKey);

        /// <summary>What this API is called in log lines.</summary>
        public string Label => "Hiro";

        /// <summary>
        /// How to supply the key, for the message shown when there is none.
        /// </summary>
        /// <remarks>
        /// Here rather than in the transport because the transport cannot know it: an environment
        /// variable is <em>this</em> API's answer, and naming the variable is the whole of the
        /// useful advice. See the remarks on <see cref="ApiKey"/> for why it is an environment
        /// variable at all, and why that makes this a development-only path.
        /// </remarks>
        public string MissingKeyAdvice
            => "Set the " + ApiKeyVariable + " environment variable and restart the editor so it "
                + "is inherited.";
    }
}
