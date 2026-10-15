using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dojo.Framework.Content
{
    /// <summary>
    /// Where content comes from, and what has to be present before the game is playable.
    /// </summary>
    /// <remarks>
    /// One root for every build: the CDN. The Addressables profile bakes a remote path in at build
    /// time; the service rewrites it at runtime to this root plus each file's name, so where content
    /// is served from is decided here and nowhere else.
    /// <para>
    /// Serialized on the root scope rather than sat on a component, because the service that reads
    /// it is not a <c>MonoBehaviour</c>.
    /// </para>
    /// <para>
    /// <b>The root lives in one place: the RootLifetimeScope prefab.</b> It has no default here on
    /// purpose. A default in code only seeds a new instance and is ignored once Unity has serialized
    /// a value, so a URL written in both places is two answers to one question, and the code's is
    /// the one nobody reads.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class ContentSettings
    {
        [Tooltip("Where every build fetches content from: the CDN folder holding the catalogue and " +
                 "bundles. No trailing slash; each file is fetched as this root plus its own name.")]
        [SerializeField] string remoteRoot = string.Empty;

        [Tooltip("Which content sets to download. Each is a self-contained set with its own " +
                 "Agents, Furniture, Managers and Thumbnails. Until sign-in supplies this, it is " +
                 "just 'default'.")]
        [SerializeField] string[] contentKeys = { DefaultKey };

        [Tooltip("Log the download to the console: where content is coming from, how big it is, " +
                 "progress as it arrives, and what ended up resident. Turn off to quieten startup.")]
        [SerializeField] bool logProgress = true;

        /// <summary>Whether the content service narrates the download to the console.</summary>
        public bool LogProgress => logProgress;

        /// <summary>
        /// The content set every player gets, and the only one there is until sign-in can say
        /// otherwise.
        /// </summary>
        public const string DefaultKey = "default";

        /// <summary>
        /// The seed entitlement: what to believe this player holds before anyone has identified
        /// them. Never empty — a configuration with nothing in it still yields
        /// <see cref="DefaultKey"/>, because a client with no content at all cannot render anything
        /// and that is never what was meant.
        /// </summary>
        /// <remarks>
        /// <b>Read-only, deliberately.</b> This is the authored default, not the live entitlement.
        /// What the player actually holds lives in <see cref="IEntitlements"/> and may only be
        /// changed by <c>EntitlementService</c>.
        /// <para>
        /// It had a public setter until the entitlement gate was given an owner, and that setter
        /// was the hole: the list decides what downloads and, through the catalogue built from it,
        /// what may be placed — so any class able to assign it was a class able to hand the player
        /// a pack they never acquired. The setter is gone rather than hidden, so the restriction is
        /// enforced by the compiler instead of by convention.
        /// </para>
        /// <para>
        /// <c>SimulatedSessionService</c> reads this and hands it back as the session's answer,
        /// which is exactly what a seed is for: with no server to ask, the person running the
        /// editor is the closest thing to one.
        /// </para>
        /// </remarks>
        public IReadOnlyList<string> ContentKeys
        {
            get
            {
                if (contentKeys == null || contentKeys.Length == 0)
                {
                    return new[] { DefaultKey };
                }

                var cleaned = new List<string>(contentKeys.Length);
                foreach (var key in contentKeys)
                {
                    if (!string.IsNullOrWhiteSpace(key) && !cleaned.Contains(key))
                    {
                        cleaned.Add(key.Trim());
                    }
                }

                return cleaned.Count == 0 ? new[] { DefaultKey } : (IReadOnlyList<string>)cleaned;
            }
        }

        /// <summary>
        /// The content root, trailing slash trimmed so callers can append without guessing. Empty
        /// when none is configured, which the service treats as "leave the built-in path alone"
        /// rather than an error.
        /// </summary>
        public string RemoteRoot => string.IsNullOrWhiteSpace(remoteRoot) ? string.Empty : remoteRoot.TrimEnd('/');
    }
}
