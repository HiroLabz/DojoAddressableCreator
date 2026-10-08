using System;
using System.Threading;
using Dojo.Framework.Events;
using Dojo.Game.Events;
using Dojo.Game.Systems;
using UnityEngine;
using VContainer.Unity;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Writes the current world back to disk shortly after anything in it changes.
    /// </summary>
    /// <remarks>
    /// Two conditions, both required, and neither is a detail.
    /// <list type="number">
    /// <item><b>The name is not this class's to choose.</b> It asks
    /// <see cref="WorldService.SaveOpenWorld"/>, which saves the open world under its own name, or
    /// a world with none yet under a World# its registry says no world has.</item>
    /// <item><b>The change has to have stopped.</b> Every change restarts a short timer and only
    /// the quiet that follows triggers the write, so dragging a desk across the room saves once
    /// when the player lets go rather than on every frame of the drag.</item>
    /// </list>
    /// <para>
    /// <b>Why the timer is not optional.</b> <see cref="WorldService.SaveAs"/> bakes every
    /// navigation surface before it writes, and nothing else in this game bakes at all — until now
    /// the only thing that ever did was the player pressing Save. Saving on each change directly
    /// would bake the navigation mesh once per frame of a drag, which is the kind of thing that
    /// turns a smooth placement gesture into a slideshow.
    /// </para>
    /// <para>
    /// An ordinary object rather than a component: it owns a subscription and a timer, reads no
    /// transform and runs nothing per frame — the waiting is <c>Awaitable</c>'s, which needs no
    /// coroutine host. The same reasoning as <see cref="WorldService"/> itself.
    /// </para>
    /// <para>
    /// <b>It never saves during teardown.</b> A pending save is cancelled on
    /// <see cref="Dispose"/> rather than flushed, because by then the scene may be half unloaded
    /// and what would be written is a world with pieces missing — overwriting a good save with a
    /// worse one is far more damaging than losing the last second of edits.
    /// </para>
    /// </remarks>
    public sealed class WorldAutoSave : IStartable, IDisposable
    {
        readonly IEventHub hub;
        readonly WorldService world;
        readonly WorldSettings settings;

        /// <summary>
        /// The phase, so that leaving Edit writes immediately rather than waiting out the timer.
        /// </summary>
        /// <remarks>
        /// Read-only: this decides when to write, never what phase the game is in.
        /// </remarks>
        readonly IGamePhase phase;

        /// <summary>
        /// True while a change has arrived that has not yet been written.
        /// </summary>
        /// <remarks>
        /// Needed because the quiet timer is not the only thing that can write now. Without it,
        /// closing the inventory would save on every close, including the ones where the player
        /// looked at the drawer and changed nothing.
        /// </remarks>
        bool dirty;

        /// <summary>Cancelled when the scope goes, so no save outlives the scene.</summary>
        readonly CancellationTokenSource life = new CancellationTokenSource();

        IDisposable subscription;

        /// <summary>The save that is waiting for the player to stop, or null.</summary>
        CancellationTokenSource waiting;

        public WorldAutoSave(IEventHub hub, WorldService world, WorldSettings settings, IGamePhase phase)
        {
            this.hub = hub;
            this.world = world;
            this.settings = settings ?? new WorldSettings();
            this.phase = phase;
        }

        public void Start()
        {
            if (!settings.AutoSave)
            {
                return;
            }

            if (hub == null || world == null)
            {
                Debug.LogWarning("[World] Auto save is on but it was not given an event hub and a "
                    + "world service, so nothing will be saved automatically.");
                return;
            }

            subscription = hub.Subscribe<WorldChanged>(OnWorldChanged);

            if (phase != null)
            {
                phase.Changed += OnPhaseChanged;
            }
        }

        /// <summary>
        /// Leaving Edit writes at once, rather than leaving the last change on the timer.
        /// </summary>
        /// <remarks>
        /// Closing the inventory is the moment the player has finished arranging, so it is the
        /// moment their work should be safe. The quiet timer still runs for changes made during a
        /// long session; this only guarantees that the session does not end with a write still
        /// pending.
        /// <para>
        /// Guarded on <see cref="dirty"/> so that opening the inventory, looking at it, and
        /// closing it again writes nothing. A save bakes every navigation surface, which is far
        /// too much work to do because somebody opened a drawer.
        /// </para>
        /// </remarks>
        void OnPhaseChanged(GamePhase current)
        {
            if (current == GamePhase.Edit || !dirty)
            {
                return;
            }

            // The pending timer is dropped rather than left to fire after this: it would write the
            // same world again a second later for no reason.
            CancelWaiting();

            var name = world.SaveOpenWorld();
            dirty = false;

            Debug.Log("[World] '" + name + "' saved on leaving Edit.");
        }

        /// <summary>
        /// Something moved: start, or restart, the wait before writing.
        /// </summary>
        void OnWorldChanged(WorldChanged message)
        {
            dirty = true;

            // The previous wait is abandoned rather than left to fire as well. This is what makes
            // a drag one save instead of dozens.
            CancelWaiting();

            waiting = CancellationTokenSource.CreateLinkedTokenSource(life.Token);

            // Fire and forget: nothing about the player's next action waits on a save, and a
            // failure is reported by the service itself.
            _ = SaveWhenQuietAsync(message.Reason, waiting.Token);
        }

        /// <summary>Waits for the changes to stop, then writes whatever world is open by then.</summary>
        /// <remarks>
        /// The name is asked for after the wait rather than before it. A second of quiet is long
        /// enough for the player to have loaded a different world, and a name taken when the timer
        /// started would save the new world's contents over the old world's entry.
        /// </remarks>
        async Awaitable SaveWhenQuietAsync(string reason, CancellationToken token)
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(settings.AutoSaveQuietSeconds, token);

                world.SaveOpenWorld();
                dirty = false;
            }
            catch (OperationCanceledException)
            {
                // Superseded by a later change, or the scene went. Either way the write that
                // matters is the one that comes after, not this one.
            }
        }

        void CancelWaiting()
        {
            if (waiting == null)
            {
                return;
            }

            waiting.Cancel();
            waiting.Dispose();
            waiting = null;
        }

        public void Dispose()
        {
            if (subscription != null)
            {
                subscription.Dispose();
                subscription = null;
            }

            if (phase != null)
            {
                phase.Changed -= OnPhaseChanged;
            }

            // Cancelled, not flushed. See the remarks on the class.
            CancelWaiting();

            life.Cancel();
            life.Dispose();
        }
    }
}
