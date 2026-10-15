using Dojo.Framework.UI;
using Dojo.Game.Placement;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The world file tray: one button to save what has been built, one to load something built
    /// earlier. The sliding, the toggle button and the remembered closed position all come from
    /// <see cref="InGamePopup"/>; this only adds the two buttons and the questions behind them.
    /// </summary>
    /// <remarks>
    /// The asking lives here and the doing lives in <see cref="WorldService"/>, which is the split
    /// that keeps either one testable: the service knows how to bake, write and rebuild a world but
    /// never puts anything on screen, and this knows which question to ask but nothing about JSON
    /// or navigation meshes. Both buttons end in a name, and a name is all the service wants.
    /// <para>
    /// The questions themselves are asked through <see cref="DialogManager"/> rather than by
    /// building a panel here, so a save dialog cannot end up on screen at the same time as a delete
    /// confirmation from placement — one owner, one modal.
    /// </para>
    /// </remarks>
    public sealed class FileSaveAndLoadUI : InGamePopup
    {
        [Header("World file")]
        [Tooltip("Lists the saved worlds and rebuilds whichever one the player picks.")]
        [SerializeField] Button loadWorldButton;

        [Tooltip("Asks which world to overwrite, or what to call a new one, and writes it.")]
        [SerializeField] Button saveWorldButton;

        IDialogManager dialogManager;
        WorldService world;

        /// <summary>
        /// Receives the dialog manager and the world service from the container.
        /// </summary>
        /// <remarks>
        /// The dialog manager is declared on the project root and brought up by the Lobby, so it
        /// resolves through this scene's scope even though the object itself is older than the
        /// scene. The world service is declared on the scene's own scope, and is a plain object
        /// rather than a component — which is why there is nothing to serialise here and nothing to
        /// go looking for either.
        /// </remarks>
        [Inject]
        public void Construct(IDialogManager dialogs, WorldService worldService)
        {
            this.dialogManager = dialogs;
            this.world = worldService;
        }

        protected override void Awake()
        {
            base.Awake();

            if (saveWorldButton != null)
            {
                saveWorldButton.onClick.AddListener(SaveWorld);
            }

            if (loadWorldButton != null)
            {
                loadWorldButton.onClick.AddListener(LoadWorld);
            }

        }

        protected override void OnDestroy()
        {
            if (saveWorldButton != null)
            {
                saveWorldButton.onClick.RemoveListener(SaveWorld);
            }

            if (loadWorldButton != null)
            {
                loadWorldButton.onClick.RemoveListener(LoadWorld);
            }

            // Last, so the base still has its own toggle button to let go of.
            base.OnDestroy();
        }

        /// <summary>
        /// Asks which world to save over, or what to call a new one, and saves it there.
        /// </summary>
        /// <remarks>
        /// Public and parameterless so it can also be dropped straight onto a Button's onClick in
        /// the inspector, the way the rest of the UI in this scene is wired.
        /// </remarks>
        public void SaveWorld()
        {
            var manager = Dialogs();

            if (world == null || manager == null)
            {
                return;   // already said why
            }

            manager.SaveAs("Save world", world.WorldNames(), name => world.SaveAs(name));
        }

        /// <summary>Lists the saved worlds and builds whichever the player picks.</summary>
        /// <remarks>
        /// Opened even when there is nothing saved yet. The chooser says so itself, which is a
        /// better answer to "why did nothing happen" than a console line the player is not reading.
        /// </remarks>
        public void LoadWorld()
        {
            var manager = Dialogs();

            if (world == null || manager == null)
            {
                return;   // already said why
            }

            manager.Choose("Load a world", world.WorldNames(), name => world.Load(name));
        }

        /// <summary>
        /// Slides the tray in until its right edge meets the parent's right edge.
        /// </summary>
        /// <remarks>
        /// Overridden because this tray is parked off the <em>right</em> of the canvas — its toggle
        /// tab pokes in at the right screen edge — while the base slides in from the left, which
        /// suits the inventory and the call tray parked off the other side. Without this the panel
        /// opens by flying across to the far left and resting where the inventory belongs, which
        /// looks far more like "it never opened" than like a panel in the wrong place.
        /// <para>
        /// Computed from the current rects rather than stored, so it survives a resolution change
        /// or a CanvasScaler rescale. Mirrors <c>AgentDetails</c>, the other right-hand panel.
        /// </para>
        /// </remarks>
        protected override Vector2 ShownPosition()
        {
            var parent = Rect.parent as RectTransform;

            if (parent == null)
            {
                return HiddenPosition;
            }

            // anchoredPosition is measured from the anchor, so locate the anchor within the parent
            // first, then offset by this tray's own pivot.
            var anchorX = parent.rect.xMin + parent.rect.width * ((Rect.anchorMin.x + Rect.anchorMax.x) * 0.5f);
            var shownX = parent.rect.xMax - anchorX - Rect.rect.width * (1f - Rect.pivot.x);

            return new Vector2(shownX, HiddenPosition.y);
        }

        /// <summary>The injected dialog manager, or null with a reason logged.</summary>
        IDialogManager Dialogs()
        {
            if (dialogManager == null)
            {
                Debug.LogError(
                    nameof(FileSaveAndLoadUI) + " on '" + name + "' was never injected, so it "
                    + "cannot ask the player anything. Register it with the scene's LifetimeScope.",
                    this);
            }

            return dialogManager;
        }
    }
}
