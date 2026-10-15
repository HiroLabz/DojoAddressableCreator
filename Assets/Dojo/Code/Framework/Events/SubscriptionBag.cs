using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dojo.Framework.Events
{
    /// <summary>
    /// Disposes whatever it holds when its GameObject is destroyed. Added automatically by
    /// <see cref="SubscriptionExtensions.DisposeWith"/>; you rarely reference it directly.
    /// </summary>
    /// <remarks>
    /// Deliberately OnDestroy and not OnDisable: a pooled or toggled object goes inactive and
    /// active again many times, and dropping its subscriptions on the first deactivate would
    /// leave it silently deaf with nothing to re-subscribe it.
    /// </remarks>
    public sealed class SubscriptionBag : MonoBehaviour
    {
        readonly List<IDisposable> items = new List<IDisposable>();

        public void Add(IDisposable disposable)
        {
            if (disposable == null)
            {
                return;
            }

            items.Add(disposable);
        }

        void OnDestroy()
        {
            for (var i = 0; i < items.Count; i++)
            {
                try
                {
                    items[i]?.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            items.Clear();
        }
    }

    public static class SubscriptionExtensions
    {
        /// <summary>
        /// Ties a subscription to <paramref name="owner"/>'s lifetime, so it unsubscribes when the
        /// GameObject is destroyed:
        /// <code>hub.Subscribe&lt;PlayerDied&gt;(OnPlayerDied).DisposeWith(this);</code>
        /// Returns the subscription so it can still be disposed early by hand.
        /// </summary>
        public static IDisposable DisposeWith(this IDisposable disposable, Component owner)
        {
            if (disposable == null)
            {
                return null;
            }

            // Unity's overloaded == reports a destroyed Component as null; there is nothing left
            // to attach to, so release immediately rather than leaking the subscription.
            if (owner == null)
            {
                disposable.Dispose();
                return disposable;
            }

            var bag = owner.GetComponent<SubscriptionBag>();
            if (bag == null)
            {
                bag = owner.gameObject.AddComponent<SubscriptionBag>();
            }

            bag.Add(disposable);
            return disposable;
        }
    }
}
