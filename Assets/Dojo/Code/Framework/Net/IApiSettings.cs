namespace Dojo.Framework.Net
{
    /// <summary>
    /// Where an API is, how to authenticate against it, and how long to wait.
    /// </summary>
    /// <remarks>
    /// What builds the generated SDK's own <c>Configuration</c> (base path, access token,
    /// timeout) for each call, without that call site needing to know whose API it is talking to:
    /// the concrete settings class belongs to the vendor layer above, implements this, and is
    /// handed down.
    /// <para>
    /// Before this interface existed, an earlier hand-rolled transport took <c>HiroApiSettings</c>
    /// directly and logged <c>"[Hiro]"</c>, which meant the one class in the project with no
    /// business knowing about any particular platform was named after one. That is the leak this
    /// closes — and the reason <see cref="Label"/> is here rather than a constant lower down.
    /// </para>
    /// <para>
    /// Read-only on purpose. Something that could write its own configuration back could disagree
    /// with the inspector.
    /// </para>
    /// </remarks>
    public interface IApiSettings
    {
        /// <summary>Base URL with no trailing slash, so path joining is unambiguous.</summary>
        string BaseUrl { get; }

        /// <summary>
        /// The bearer token to send, or null when there is none.
        /// </summary>
        /// <remarks>
        /// A property read at the moment of the request rather than a value captured once, because
        /// an implementation may well be reading it from somewhere that can change — an
        /// environment variable, a keychain, a token that was refreshed. The transport must not
        /// cache what it gets back.
        /// </remarks>
        string ApiKey { get; }

        /// <summary>
        /// True when there is a key to authenticate with.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="ApiKey"/> being non-empty so an implementation with a
        /// different notion of "ready" — two credentials, an expiry to check — can say so without
        /// the transport having to guess.
        /// </remarks>
        bool HasKey { get; }

        /// <summary>Seconds a plain request may take before it is abandoned.</summary>
        int RequestTimeoutSeconds { get; }

        /// <summary>Seconds a streamed answer may take. Normally far longer than a plain one.</summary>
        int StreamTimeoutSeconds { get; }

        /// <summary>Whether every request and its status is logged.</summary>
        bool LogRequests { get; }

        /// <summary>
        /// Short name for this API in log lines — "Hiro", "Steam".
        /// </summary>
        /// <remarks>
        /// So a project talking to two services can tell their logs apart, and so the transport
        /// needs no constant of its own naming a platform it should not know about.
        /// </remarks>
        string Label { get; }

        /// <summary>
        /// What to tell somebody whose key is missing, in terms of how this API's key is supplied.
        /// </summary>
        /// <remarks>
        /// The transport can say "there is no key" but not how to fix it — one API reads an
        /// environment variable, another wants a login. So the advice comes from the settings,
        /// which is the only thing that knows.
        /// </remarks>
        string MissingKeyAdvice { get; }
    }
}
