using System;

namespace Dojo.Framework.Events
{
    /// <summary>
    /// App-wide type-keyed publish/subscribe. One message type is one channel, so there are no
    /// string keys to typo and no central enum to keep in sync.
    /// </summary>
    /// <remarks>
    /// Main thread only. Dispatch is synchronous: when <see cref="Publish{T}"/> returns, every
    /// handler has run. A handler that throws is logged and the remaining handlers still receive
    /// the message.
    /// </remarks>
    public interface IEventHub
    {
        /// <summary>
        /// Registers <paramref name="handler"/> for messages of type <typeparamref name="T"/>.
        /// Dispose the result to unsubscribe — see <c>DisposeWith</c> for the MonoBehaviour case.
        /// </summary>
        IDisposable Subscribe<T>(Action<T> handler);

        /// <summary>
        /// Subscribes and, if a sticky value has already been published for
        /// <typeparamref name="T"/>, immediately delivers it. Use for state-shaped messages where
        /// a late subscriber still needs the current value.
        /// </summary>
        IDisposable SubscribeSticky<T>(Action<T> handler, bool invokeImmediately = true);

        /// <summary>Delivers a message to current subscribers. No subscribers is not an error.</summary>
        void Publish<T>(T message);

        /// <summary>
        /// Publishes and retains the message so later <see cref="SubscribeSticky{T}"/> callers
        /// receive it. Once a type is sticky, plain <see cref="Publish{T}"/> keeps the retained
        /// value current rather than letting it go stale.
        /// </summary>
        void PublishSticky<T>(T message);

        /// <summary>Reads the retained value for <typeparamref name="T"/> without subscribing.</summary>
        bool TryGetLast<T>(out T last);

        /// <summary>Drops all subscribers and any retained value for one message type.</summary>
        void Clear<T>();

        /// <summary>Drops every subscriber and retained value across all message types.</summary>
        void ClearAll();
    }
}
