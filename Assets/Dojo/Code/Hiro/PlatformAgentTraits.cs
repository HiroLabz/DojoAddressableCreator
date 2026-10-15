using System;

namespace Dojo.Hiro
{
    /// <summary>Where a platform agent came from. The API's <c>origin</c> field.</summary>
    /// <remarks>
    /// Three separate enums rather than one, because the platform reports three separate fields
    /// and an agent carries a value from each at the same time: the one measured was
    /// <c>origin: "builder"</c>, <c>status: "draft"</c>, <c>isolation_mode: "shared"</c> — all
    /// three at once. Folding them into a single enum would have forced a choice between them and
    /// thrown the other two away.
    /// <para>
    /// Every one has an <c>Unknown</c> member, and it is deliberately the default. The platform can
    /// add a value at any time without telling this project, and an unrecognised string landing on
    /// <c>Unknown</c> is a game that keeps working; the alternative is a parse that throws on a
    /// field nothing actually branches on.
    /// </para>
    /// </remarks>
    public enum AgentOrigin
    {
        Unknown,

        /// <summary>Built in the console's agent builder.</summary>
        Builder,

        /// <summary>Installed from the marketplace.</summary>
        Marketplace,

        /// <summary>Created through the API.</summary>
        Api,
    }

    /// <summary>How far along a platform agent is. The API's <c>status</c> field.</summary>
    public enum AgentStatus
    {
        Unknown,

        /// <summary>Still being worked on. It still answers — nothing is gated on this.</summary>
        Draft,

        /// <summary>Published and live.</summary>
        Active,

        /// <summary>Switched off by whoever owns it.</summary>
        Disabled,

        /// <summary>Kept for the record but no longer in use.</summary>
        Archived,
    }

    /// <summary>Whether a platform agent runs on shared or dedicated capacity.</summary>
    public enum AgentIsolation
    {
        Unknown,

        /// <summary>Shares runtime capacity with other agents.</summary>
        Shared,

        /// <summary>Has capacity of its own.</summary>
        Dedicated,
    }

    /// <summary>
    /// Turns the platform's string fields into the enums above.
    /// </summary>
    /// <remarks>
    /// Case-insensitive, and unrecognised input becomes <c>Unknown</c> rather than throwing. The
    /// strings arrive from somebody else's service, and the correct response to a value this build
    /// has never heard of is to carry on — not to take down the roster over a label.
    /// </remarks>
    public static class PlatformAgentTraits
    {
        public static AgentOrigin ParseOrigin(string text) => Parse<AgentOrigin>(text);

        public static AgentStatus ParseStatus(string text) => Parse<AgentStatus>(text);

        public static AgentIsolation ParseIsolation(string text) => Parse<AgentIsolation>(text);

        static T Parse<T>(string text) where T : struct
        {
            if (string.IsNullOrEmpty(text))
            {
                return default(T);   // Unknown, which is first in each enum
            }

            T parsed;
            return Enum.TryParse(text.Trim(), true, out parsed) ? parsed : default(T);
        }
    }
}
