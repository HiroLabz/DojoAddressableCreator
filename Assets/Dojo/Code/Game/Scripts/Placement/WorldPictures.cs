using System;
using System.Threading;
using Dojo.Game.Systems;
using UnityEngine;
using VContainer.Unity;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Takes the picture of the world the Lobby's continue card shows: each time the inventory
    /// closes, and on BACK, just before the Lobby loads.
    /// </summary>
    /// <remarks>
    /// Closing the inventory is when the player has finished arranging, so it is when the room is
    /// worth a picture - the same moment <see cref="WorldAutoSave"/> saves it. The picture waits
    /// one frame for Edit's own pieces, the grid and the ghost, to go. BACK takes it at once: the
    /// scene is about to go, and the menu it was pressed on is left out anyway.
    /// <para>
    /// Rendered from the main camera without the UI layer, so the menus, the HUD and the tour card
    /// are not in it. The in-game background is on another layer, so it is.
    /// </para>
    /// </remarks>
    public sealed class WorldPictures : IStartable, IDisposable
    {
        /// <summary>Twice the continue card's 288 × 200 picture box, so it stays sharp.</summary>
        public const int Width = 576;
        public const int Height = 400;

        readonly IGamePhase phase;
        readonly WorldService world;
        readonly WorldPictureFile file = WorldPictureFile.InPersistentData();

        /// <summary>Cancelled when the scope goes, so a picture waiting a frame is not taken of a scene half unloaded.</summary>
        readonly CancellationTokenSource life = new CancellationTokenSource();

        bool wasEdit;

        public WorldPictures(IGamePhase phase, WorldService world)
        {
            this.phase = phase;
            this.world = world;
        }

        public void Start()
        {
            if (phase == null)
            {
                Debug.LogWarning("[World] No game phase, so no picture is taken when the inventory closes.");
                return;
            }

            wasEdit = phase.IsEdit;
            phase.Changed += OnPhaseChanged;
        }

        void OnPhaseChanged(GamePhase current)
        {
            var leftEdit = wasEdit && current != GamePhase.Edit;
            wasEdit = current == GamePhase.Edit;

            if (leftEdit)
            {
                _ = TakeNextFrameAsync(life.Token);
            }
        }

        async Awaitable TakeNextFrameAsync(CancellationToken token)
        {
            try
            {
                await Awaitable.NextFrameAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Take();
        }

        /// <summary>Takes the picture of the open world now, and writes it over that world's last one.</summary>
        /// <remarks>
        /// A world with no name yet - new, or the default one, before its first save - has nothing
        /// to file a picture under, so it gets none; the Lobby shows the default world's for it.
        /// Leaving the inventory saves before this runs, a frame later, so a world named by that
        /// save has its picture taken under the new name.
        /// </remarks>
        public void Take()
        {
            var worldName = world != null ? world.OpenWorldName : null;

            if (string.IsNullOrEmpty(worldName))
            {
                Debug.Log("[World] This world has not been saved yet, so it has no name to file a picture under.");
                return;
            }

            var camera = Camera.main;

            if (camera == null)
            {
                Debug.LogWarning("[World] No main camera, so no picture of the world was taken.");
                return;
            }

            var uiLayer = LayerMask.NameToLayer("UI");
            Texture2D picture = null;

            try
            {
                picture = WorldPicture.Render(camera, Width, Height, uiLayer >= 0 ? 1 << uiLayer : 0);
                file.Write(worldName, picture.EncodeToPNG());
            }
            catch (Exception exception)
            {
                // A missing picture costs the card its image and nothing else, so it is said and
                // left there rather than allowed to stop BACK.
                Debug.LogWarning("[World] The picture of the world could not be taken: " + exception.Message);
            }
            finally
            {
                if (picture != null)
                {
                    UnityEngine.Object.Destroy(picture);
                }
            }
        }

        public void Dispose()
        {
            if (phase != null)
            {
                phase.Changed -= OnPhaseChanged;
            }

            life.Cancel();
            life.Dispose();
        }
    }
}
