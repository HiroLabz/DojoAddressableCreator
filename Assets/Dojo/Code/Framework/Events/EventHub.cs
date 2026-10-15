using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Dojo.Framework.Events
{
    /// <summary>
    /// Default <see cref="IEventHub"/>. Register once as a container singleton; being an instance
    /// rather than a static means it is mockable in tests and rebuilt cleanly on every play
    /// session, even with Domain Reload disabled.
    /// </summary>
    public sealed class EventHub : IEventHub, IDisposable
    {
        readonly Dictionary<Type, TopicBase> topics = new Dictionary<Type, TopicBase>();
        readonly int mainThreadId;

        public EventHub()
        {
            // The container is built on the main thread, so this is the thread to compare against.
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            AssertMainThread();
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            GetOrCreateTopic<T>().Add(handler);
            return new Subscription<T>(this, handler);
        }

        public IDisposable SubscribeSticky<T>(Action<T> handler, bool invokeImmediately = true)
        {
            AssertMainThread();
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var topic = GetOrCreateTopic<T>();
            topic.Add(handler);

            if (invokeImmediately && topic.HasSticky)
            {
                Invoke(handler, topic.Last);
            }

            return new Subscription<T>(this, handler);
        }

        public void Publish<T>(T message)
        {
            AssertMainThread();

            if (!topics.TryGetValue(typeof(T), out var untyped))
            {
                return;
            }

            var topic = (Topic<T>)untyped;

            // Keep an already-sticky channel current, so a late subscriber can never be handed a
            // value that plain Publish has since superseded.
            if (topic.HasSticky)
            {
                topic.SetSticky(message);
            }

            topic.Publish(message);
        }

        public void PublishSticky<T>(T message)
        {
            AssertMainThread();

            var topic = GetOrCreateTopic<T>();
            topic.SetSticky(message);
            topic.Publish(message);
        }

        public bool TryGetLast<T>(out T last)
        {
            if (topics.TryGetValue(typeof(T), out var untyped))
            {
                var topic = (Topic<T>)untyped;
                if (topic.HasSticky)
                {
                    last = topic.Last;
                    return true;
                }
            }

            last = default;
            return false;
        }

        public void Clear<T>()
        {
            if (topics.TryGetValue(typeof(T), out var topic))
            {
                topic.Clear();
                topics.Remove(typeof(T));
            }
        }

        public void ClearAll()
        {
            // No reflection: TopicBase exposes a non-generic Clear, so nothing here needs a
            // MakeGenericMethod call that IL2CPP may not have generated for a value-type T.
            foreach (var topic in topics.Values)
            {
                topic.Clear();
            }

            topics.Clear();
        }

        public void Dispose() => ClearAll();

        Topic<T> GetOrCreateTopic<T>()
        {
            if (topics.TryGetValue(typeof(T), out var existing))
            {
                return (Topic<T>)existing;
            }

            var created = new Topic<T>();
            topics[typeof(T)] = created;
            return created;
        }

        void Unsubscribe<T>(Action<T> handler)
        {
            if (topics.TryGetValue(typeof(T), out var topic))
            {
                ((Topic<T>)topic).Remove(handler);
            }
        }

        static void Invoke<T>(Action<T> handler, T message)
        {
            // One bad subscriber must not deny the message to the others, nor surface as an
            // exception inside whatever published it.
            try
            {
                handler(message);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        void AssertMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
            {
                return;
            }

            Debug.LogError(
                $"[{nameof(EventHub)}] Used from a background thread " +
                $"(tid={Thread.CurrentThread.ManagedThreadId}, expected {mainThreadId}). " +
                "The hub is main-thread only.");
        }

        abstract class TopicBase
        {
            public abstract void Clear();
        }

        sealed class Topic<T> : TopicBase
        {
            readonly List<Action<T>> handlers = new List<Action<T>>();

            // Dispatch runs over a snapshot so handlers may subscribe or unsubscribe while a
            // message is being delivered. Rebuilt only when the handler set actually changes,
            // so a steady-state publish allocates nothing.
            Action<T>[] snapshot = Array.Empty<Action<T>>();
            bool snapshotStale;

            public bool HasSticky { get; private set; }

            public T Last { get; private set; }

            public void Add(Action<T> handler)
            {
                handlers.Add(handler);
                snapshotStale = true;
            }

            public void Remove(Action<T> handler)
            {
                if (handlers.Remove(handler))
                {
                    snapshotStale = true;
                }
            }

            public void SetSticky(T message)
            {
                HasSticky = true;
                Last = message;
            }

            public void Publish(T message)
            {
                if (snapshotStale)
                {
                    snapshot = handlers.ToArray();
                    snapshotStale = false;
                }

                var current = snapshot;
                for (var i = 0; i < current.Length; i++)
                {
                    Invoke(current[i], message);
                }
            }

            public override void Clear()
            {
                handlers.Clear();
                snapshot = Array.Empty<Action<T>>();
                snapshotStale = false;
                HasSticky = false;
                Last = default;
            }
        }

        /// <summary>
        /// Class rather than struct: returning a struct as <see cref="IDisposable"/> would box on
        /// every subscribe, and the null-out below makes a second Dispose a no-op instead of
        /// silently cancelling an unrelated later subscription to the same handler.
        /// </summary>
        sealed class Subscription<T> : IDisposable
        {
            EventHub hub;
            Action<T> handler;

            public Subscription(EventHub hub, Action<T> handler)
            {
                this.hub = hub;
                this.handler = handler;
            }

            public void Dispose()
            {
                if (hub == null)
                {
                    return;
                }

                hub.Unsubscribe(handler);
                hub = null;
                handler = null;
            }
        }
    }
}
