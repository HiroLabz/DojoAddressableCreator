using Dojo.Game.InGame.Controllers;
using Dojo.Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The view cube, top right while the inventory is open: Top, Front, Back, Left and Right around
    /// Perspective in the middle, the game's own view. The view in force is lit.
    /// </summary>
    /// <remarks>
    /// Buttons only; <see cref="CameraViews"/> does the moving and the hiding. In a side view a note
    /// under the cube says why nothing can be placed or painted there. Injected by
    /// <c>GameLifetimeScope</c>.
    /// </remarks>
    public sealed class ViewCube : MonoBehaviour
    {
        [Tooltip("The cube and its note. Shown while the inventory is open.")]
        [SerializeField] GameObject panel;

        [SerializeField] Button perspectiveButton;
        [SerializeField] Button topButton;
        [SerializeField] Button frontButton;
        [SerializeField] Button backButton;
        [SerializeField] Button leftButton;
        [SerializeField] Button rightButton;

        [Tooltip("The face of a view not in force.")]
        [SerializeField] Sprite idleFace;

        [Tooltip("The face of the view in force.")]
        [SerializeField] Sprite selectedFace;

        [Tooltip("Shown in the side views: look only.")]
        [SerializeField] TMP_Text lookOnlyNote;

        CameraViews views;
        IGamePhase phase;

        [Inject]
        public void Construct(CameraViews cameraViews, IGamePhase gamePhase)
        {
            views = cameraViews;
            phase = gamePhase;

            if (views != null) views.Changed += OnViewChanged;
            if (phase != null) phase.Changed += OnPhaseChanged;

            Refresh();
        }

        void Awake()
        {
            Wire(perspectiveButton, CameraView.Perspective);
            Wire(topButton, CameraView.Top);
            Wire(frontButton, CameraView.Front);
            Wire(backButton, CameraView.Back);
            Wire(leftButton, CameraView.Left);
            Wire(rightButton, CameraView.Right);

            Refresh();
        }

        void OnDestroy()
        {
            if (views != null) views.Changed -= OnViewChanged;
            if (phase != null) phase.Changed -= OnPhaseChanged;
        }

        void Wire(Button button, CameraView view)
        {
            if (button != null)
            {
                button.onClick.AddListener(() =>
                {
                    if (views != null)
                    {
                        views.Choose(view);
                    }
                });
            }
        }

        void OnViewChanged(CameraView _) => Refresh();

        void OnPhaseChanged(GamePhase _) => Refresh();

        void Refresh()
        {
            var editing = phase != null && phase.IsEdit && views != null;

            if (panel != null && panel.activeSelf != editing)
            {
                panel.SetActive(editing);
            }

            var current = views != null ? views.Current : CameraView.Perspective;

            Face(perspectiveButton, current == CameraView.Perspective);
            Face(topButton, current == CameraView.Top);
            Face(frontButton, current == CameraView.Front);
            Face(backButton, current == CameraView.Back);
            Face(leftButton, current == CameraView.Left);
            Face(rightButton, current == CameraView.Right);

            if (lookOnlyNote != null)
            {
                lookOnlyNote.gameObject.SetActive(editing && CameraViews.IsSide(current));
            }
        }

        void Face(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            var image = button.targetGraphic as Image;
            var sprite = selected ? selectedFace : idleFace;

            if (image != null && sprite != null)
            {
                image.sprite = sprite;
            }
        }
    }
}
