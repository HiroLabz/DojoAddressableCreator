using System;
using UnityEngine;

namespace Dojo.Framework.Net
{
    /// <summary>
    /// Where the SpacetimeDB module lives, and whether to talk to it at all.
    /// </summary>
    /// <remarks>
    /// Serialized on the root scope beside <c>ContentSettings</c>, and for the same reason: the
    /// service that reads it is not a <c>MonoBehaviour</c>, and switching host without a rebuild is
    /// worth more than the tidiness of a component.
    /// <para>
    /// <see cref="Enabled"/> defaults to off. The module is not required for a single-player
    /// session — content downloads and the inventory work without it — so a developer who has no
    /// host running should get a game, not a wall of connection errors.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class SpacetimeSettings
    {
        [Tooltip("Off means the app never opens a connection and uses the no-backend path. " +
                 "Nothing else changes.")]
        [SerializeField] bool enabled;

        [Tooltip("Host the module is published to. No trailing slash.")]
        [SerializeField] string host = "http://127.0.0.1:3000";

        [Tooltip("Database name the module was published under.")]
        [SerializeField] string dbName = "dojo-game";

        [Tooltip("Seconds to wait for the connection before giving up and carrying on without it.")]
        [SerializeField] float connectTimeoutSeconds = 10f;

        public bool Enabled => enabled;

        public string Host => string.IsNullOrWhiteSpace(host)
            ? "http://127.0.0.1:3000"
            : host.TrimEnd('/');

        public string DbName => string.IsNullOrWhiteSpace(dbName) ? "dojo-game" : dbName.Trim();

        /// <summary>
        /// How long to wait before carrying on without a connection.
        /// </summary>
        /// <remarks>
        /// Floored rather than trusted, because a zero left in the Inspector would mean "time out
        /// immediately", which reads as "the server is always down" and is a miserable thing to
        /// debug.
        /// </remarks>
        public float ConnectTimeoutSeconds => connectTimeoutSeconds < 1f ? 10f : connectTimeoutSeconds;

        public override string ToString() => Host + "/" + DbName;
    }
}
