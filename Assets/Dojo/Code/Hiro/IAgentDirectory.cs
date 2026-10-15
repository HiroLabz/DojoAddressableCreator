using System;
using System.Collections.Generic;
using System.Threading;
using Dojo.Framework.Net;
using Newtonsoft.Json;
using UnityEngine;

namespace Dojo.Hiro
{
    /// <summary>
    /// Who the platform says the agents are.
    /// </summary>
    /// <remarks>
    /// A domain interface: it says what is wanted, not how it arrives. That is what lets the
    /// transport underneath change — REST client to generated SDK, or a socket later — without any
    /// caller noticing.
    /// </remarks>
    public interface IAgentDirectory
    {
        /// <summary>
        /// Every agent on the platform, or a failure carrying the reason.
        /// </summary>
        /// <remarks>
        /// Still never a throw — the failure is a value, matching <see cref="ApiResult"/>'s own
        /// reasoning that an ordinary 401 is an answer rather than something to unwind a stack
        /// over. What changed is that the failure is no longer flattened into an empty list: the
        /// roster is now a required readiness gate, and a gate cannot distinguish "the tenant has
        /// no agents" from "the call did not happen" unless the answer says which it was.
        /// </remarks>
        Awaitable<AgentListResult> ListAsync(CancellationToken token);
    }

    /// <summary>
    /// One attempt at reading the roster: the agents, or why there are none.
    /// </summary>
    /// <remarks>
    /// Deliberately shaped like <see cref="ApiResult"/> — <see cref="Ok"/> keyed off a null
    /// <see cref="Error"/>, the API's own <see cref="Code"/> kept apart from the message so a
    /// caller can branch on one and show the other. A successful call with no agents is
    /// <see cref="Ok"/> and empty, which is the distinction this type exists to preserve.
    /// </remarks>
    public readonly struct AgentListResult
    {
        static readonly RemoteAgent[] None = new RemoteAgent[0];

        /// <summary>The agents. Never null; empty on any failure.</summary>
        public readonly IReadOnlyList<RemoteAgent> Agents;

        /// <summary>Why it failed, in a form worth putting in front of a person. Null on success.</summary>
        public readonly string Error;

        /// <summary>The API's own error code, or null.</summary>
        public readonly string Code;

        /// <summary>HTTP status, or 0 when the request never reached the server.</summary>
        public readonly long Status;

        AgentListResult(IReadOnlyList<RemoteAgent> agents, string error, string code, long status)
        {
            Agents = agents ?? None;
            Error = error;
            Code = code;
            Status = status;
        }

        /// <summary>True when the call reached the API and it answered with a roster.</summary>
        public bool Ok => Error == null;

        /// <summary>How many agents came back. Zero on a failure and on an empty tenant alike.</summary>
        public int Count => Agents == null ? 0 : Agents.Count;

        public static AgentListResult Success(IReadOnlyList<RemoteAgent> agents)
            => new AgentListResult(agents, null, null, 200);

        public static AgentListResult Failure(string error, string code, long status)
            => new AgentListResult(None, error ?? "The agent roster could not be read.", code, status);
    }

    /// <summary>
    /// One agent as the platform describes it.
    /// </summary>
    /// <remarks>
    /// A deliberately small subset of what <c>/v1/agents</c> returns. The full record carries
    /// thirty-odd fields — guardrails, budgets, tool allowlists, model profiles — and every one
    /// taken in here is a field this project would have to keep in step with somebody else's
    /// schema. These five are what the game actually uses: the id to talk to, the name and blurb
    /// to show, and the status and slug to tell one from another.
    /// <para>
    /// Newtonsoft rather than <c>JsonUtility</c>, because <c>/v1/agents</c> returns a bare array at
    /// the top level and <c>JsonUtility</c> cannot deserialise one at all. Everything about the
    /// local <c>agents.json</c> stays on JsonUtility, which is fine — that file is an object.
    /// </para>
    /// </remarks>
    public sealed class RemoteAgent
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>Where the agent came from: builder, marketplace, api.</summary>
        [JsonProperty("origin")]
        public string Origin { get; set; }

        /// <summary>Shared or dedicated runtime capacity.</summary>
        [JsonProperty("isolation_mode")]
        public string IsolationMode { get; set; }

        /// <summary>
        /// What the platform says this agent can do — "assistant", "research", "summarize".
        /// </summary>
        /// <remarks>
        /// Taken in for one reason: it is the closest thing the platform has to our
        /// <c>workId</c>. There is no department field on a platform agent — the payload was
        /// checked field by field, and the only candidates were <c>slug</c> (a URL fragment),
        /// <c>origin</c> ("builder") and <c>status</c> ("draft"), none of which mean what a
        /// department means. Modules at least describe the job.
        /// </remarks>
        [JsonProperty("enabled_modules")]
        public string[] Modules { get; set; }

        /// <summary>The first module, or empty. What a new roster entry seeds its department from.</summary>
        public string PrimaryModule
            => Modules != null && Modules.Length > 0 ? Modules[0] : string.Empty;

        public override string ToString()
            => (Name ?? "?") + " [" + (Status ?? "?") + "] " + (Id ?? "?");
    }

    /// <summary>
    /// The directory for a build with no Hiro backend: the roster is never read from the platform,
    /// so the registry keeps whatever the local credentials file holds.
    /// </summary>
    public sealed class OfflineAgentDirectory : IAgentDirectory
    {
        public Awaitable<AgentListResult> ListAsync(CancellationToken token)
        {
            var source = new AwaitableCompletionSource<AgentListResult>();
            source.SetResult(AgentListResult.Failure(
                "There is no Hiro backend in this build.", "offline", 0));
            return source.Awaitable;
        }
    }
}
