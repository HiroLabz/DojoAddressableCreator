using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Dojo.Framework.Session
{
    /// <summary>
    /// What the server says when the app connects: who this is, and what they are entitled to.
    /// </summary>
    /// <remarks>
    /// Deliberately shaped after the response ARCH-001 §2 already specifies, so the real
    /// implementation is a deserialisation rather than a redesign. <see cref="ContentKeys"/> is the
    /// one field that spec does not yet carry — adding it there is a revision, not a new concept.
    /// </remarks>
    public sealed class SessionResult
    {
        /// <summary>False when the server could not be reached or refused the session.</summary>
        public bool Connected { get; }

        /// <summary>Who the server says this is. Empty before real sign-in exists.</summary>
        public string PlayerId { get; }

        /// <summary>
        /// The content sets this player may download, in priority order. Never empty on a
        /// successful connection — a player entitled to nothing still gets the default set.
        /// </summary>
        public IReadOnlyList<string> ContentKeys { get; }

        /// <summary>Why the connection failed, for the startup error popup.</summary>
        public string Error { get; }

        SessionResult(bool connected, string playerId, IReadOnlyList<string> contentKeys, string error)
        {
            Connected = connected;
            PlayerId = playerId ?? string.Empty;
            ContentKeys = contentKeys ?? new string[0];
            Error = error ?? string.Empty;
        }

        public static SessionResult Success(string playerId, IReadOnlyList<string> contentKeys)
            => new SessionResult(true, playerId, contentKeys, null);

        public static SessionResult Failure(string error)
            => new SessionResult(false, null, null, error);
    }

    /// <summary>
    /// The app's connection to the server, and the first thing startup does.
    /// </summary>
    /// <remarks>
    /// This is the "connection" step in the startup sequence: connect, find out who the player is
    /// and what content they hold, and only then download anything. It is named for the session
    /// rather than the connection to keep it distinct from ARCH-001's <c>ISpacetimeConnection</c>,
    /// which is a different connection made later and for a different reason.
    /// <para>
    /// One interface, swappable implementations, chosen at registration — the same shape the
    /// transport uses. <c>SimulatedSessionService</c> answers locally while no server exists; a REST
    /// implementation replaces it without touching a single call site.
    /// </para>
    /// </remarks>
    public interface ISessionService
    {
        /// <summary>The last result, or null before <see cref="ConnectAsync"/> has returned.</summary>
        SessionResult Current { get; }

        /// <summary>True once a connection has succeeded.</summary>
        bool IsConnected { get; }

        /// <summary>
        /// Opens the session. Never throws for an ordinary failure — an unreachable server comes
        /// back as <see cref="SessionResult.Failure"/> so startup can report it and decide, rather
        /// than unwinding through a fire-and-forget call where nothing is listening.
        /// </summary>
        Awaitable<SessionResult> ConnectAsync(CancellationToken cancellationToken = default);
    }
}
