using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// The loading screen: one canvas, put up over whatever is running and kept across every scene
    /// load for as long as the process lives.
    /// </summary>
    /// <remarks>
    /// <b>Authored only.</b> Every part comes from <c>LoadingScreen.prefab</c> and a missing
    /// reference is reported rather than papered over, the same arrangement the dialogs use.
    /// <para>
    /// <c>DontDestroyOnLoad</c> is the whole reason this is a service rather than something a scene
    /// owns. The screen's job is to cover a scene change, and anything living in the outgoing scene
    /// is destroyed by the very transition it was put up to hide.
    /// </para>
    /// <para>
    /// The bar is smoothed towards its target rather than snapped to it. Progress from a download
    /// arrives in lumps — a chunk completes and the number jumps ten points — and a bar that
    /// teleports reads as broken, while the same jump eased over a fraction of a second reads as
    /// fast. It is never eased backwards, and it always lands exactly on 1 before the screen goes.
    /// </para>
    /// </remarks>
    public sealed class LoadingScreen : MonoBehaviour, ILoadingScreen
    {
        [Header("Chrome")]
        [Tooltip("Turned on and off to show and hide the screen.")]
        [SerializeField] GameObject root;

        [Tooltip("Faded in and out. Lives on the same object as the root.")]
        [SerializeField] CanvasGroup group;

        [Tooltip("The big line, e.g. 'STUDIO FLOOR'.")]
        [SerializeField] TMP_Text title;

        [Tooltip("The line under it.")]
        [SerializeField] TMP_Text subtitle;

        [Header("Progress")]
        [Tooltip("The stage name above the bar, e.g. 'PLACING PIECES'.")]
        [SerializeField] TMP_Text stageLabel;

        [Tooltip("The percentage on the right of the stage row.")]
        [SerializeField] TMP_Text percentLabel;

        [Tooltip("The filled part of the bar. Driven by its width, so it wants a left-anchored rect.")]
        [SerializeField] RectTransform fill;

        [Tooltip("The bar's full width. Defaults to the fill's parent.")]
        [SerializeField] RectTransform track;

        [Tooltip("The small line under the bar on the left, e.g. '34 pieces · 3 agents'.")]
        [SerializeField] TMP_Text detailLabel;

        [Tooltip("The small line under the bar on the right, e.g. 'STEP 2 / 4'.")]
        [SerializeField] TMP_Text stepLabel;

        [Header("Steps")]
        [Tooltip("Dots are created under this. Wants a horizontal layout group.")]
        [SerializeField] RectTransform dots;

        [Tooltip("The dot prefab. Authored inactive — it is a template among the things it makes.")]
        [SerializeField] Image dotPrefab;

        [Tooltip("Sprite for a step that is done or running.")]
        [SerializeField] Sprite dotDone;

        [Tooltip("Sprite for a step still to come.")]
        [SerializeField] Sprite dotTodo;

        [Tooltip("The caption beside the dots, e.g. 'Pieces placed'.")]
        [SerializeField] TMP_Text dotsCaption;

        [Header("Tip")]
        [Tooltip("The whole tip card. Hidden when a run carries no tip.")]
        [SerializeField] GameObject tipCard;

        [Tooltip("The tip's body text.")]
        [SerializeField] TMP_Text tipLabel;

        [Header("Corners")]
        [Tooltip("Bottom-left. Filled with the application version at startup.")]
        [SerializeField] TMP_Text versionLabel;

        [Tooltip("Bottom-right. Shown only while the run can actually be cancelled.")]
        [SerializeField] GameObject cancelHint;

        [Header("Feel")]
        [Tooltip("Seconds the fade in and out take.")]
        [SerializeField] float fadeDuration = 0.25f;

        [Tooltip("How quickly the bar catches up to the real value. Higher is snappier.")]
        [SerializeField] float barCatchUp = 6f;

        [Tooltip("Show the current step's own progress rather than progress through the whole run. " +
                 "Off, the bar never restarts; on, it restarts at every step.")]
        [SerializeField] bool showStepProgress;

        /// <inheritdoc />
        public bool IsShowing => root != null && root.activeSelf;

        /// <inheritdoc />
        public LoadingRun Current { get; private set; }

        readonly List<Image> dotPool = new List<Image>();

        // What the bar is drawing, as opposed to what the run reports. The gap between them is the
        // smoothing.
        float shownProgress;

        float fadeTarget;
        bool dirty;

        void Awake()
        {
            if (group == null && root != null)
            {
                group = root.GetComponent<CanvasGroup>();
            }

            if (track == null && fill != null)
            {
                track = fill.parent as RectTransform;
            }

            if (root == null || fill == null || dots == null || dotPrefab == null)
            {
                Debug.LogError(
                    $"{nameof(LoadingScreen)} on '{name}' is missing authored parts — it needs at " +
                    "least root, fill, dots and dotPrefab. Assign them on LoadingScreen.prefab.",
                    this);
                return;
            }

            if (versionLabel != null)
            {
                versionLabel.text = Application.version;
            }

            // Only in play mode: Unity refuses the call outside it, and there are no scene loads to
            // survive there anyway.
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            HideImmediate();
        }

        /// <inheritdoc />
        public LoadingRun Begin(LoadingRequest request)
        {
            // One screen, so one run. The old one is completed rather than dropped, or whatever was
            // waiting on it would wait for a completion that never comes.
            if (Current != null && !Current.IsComplete)
            {
                Debug.LogWarning(
                    string.Format(
                        "{0}: a load titled '{1}' was still running when '{2}' began. The first one " +
                        "has been completed to make room. Concurrent work belongs in a step's job.",
                        name,
                        Current.Title,
                        request.Title),
                    this);

                Current.Complete();
            }

            Detach();

            var run = new LoadingRun(request);
            Current = run;
            run.Changed += OnRunChanged;

            BuildDots(run);

            // From zero, not from whatever the last load left behind — a bar that opens at 80%
            // because the previous load ended there is describing work nobody is doing.
            shownProgress = 0f;

            Show();
            Redraw();

            return run;
        }

        void OnRunChanged()
        {
            // Marked rather than drawn: a step with several jobs reporting raises this many times a
            // frame, and the screen only has to be right once per frame.
            dirty = true;
        }

        void Update()
        {
            LoadingRun run = Current;

            if (run == null)
            {
                Fade();
                return;
            }

            if (run.Cancellable && !run.IsComplete && EscapePressed())
            {
                run.Cancel();
            }

            if (dirty)
            {
                dirty = false;
                Redraw();
            }

            float target = Target(run);

            // Eased towards, and never backwards — see the class remarks.
            if (shownProgress < target)
            {
                shownProgress = Mathf.Lerp(shownProgress, target, 1f - Mathf.Exp(-barCatchUp * Time.unscaledDeltaTime));

                // Lerp approaches without arriving, so the last fraction is closed by hand.
                if (target - shownProgress < 0.001f)
                {
                    shownProgress = target;
                }
            }
            else if (target > shownProgress)
            {
                shownProgress = target;
            }

            ApplyBar();

            // The screen leaves only once the run is done, the bar has caught up with it, and
            // nothing is holding it. Leaving while the bar still reads 80% shows the player a number
            // that was never true.
            if (run.IsComplete && !run.IsHeld && shownProgress >= 0.999f)
            {
                Detach();
                Current = null;
                Hide();
            }

            Fade();
        }

        static bool EscapePressed()
        {
            Keyboard keyboard = Keyboard.current;

            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
        }

        float Target(LoadingRun run)
        {
            if (run.IsComplete)
            {
                return 1f;
            }

            if (!showStepProgress)
            {
                return run.Overall;
            }

            LoadingStep step = run.Current;

            return step == null ? 0f : step.Progress;
        }

        void Redraw()
        {
            LoadingRun run = Current;

            if (run == null)
            {
                return;
            }

            if (title != null)
            {
                title.text = run.Title;
            }

            if (subtitle != null)
            {
                subtitle.text = run.Subtitle;
            }

            LoadingStep step = run.Current;

            if (stageLabel != null)
            {
                stageLabel.text = step == null ? string.Empty : step.Label;
            }

            if (detailLabel != null)
            {
                detailLabel.text = run.Detail;
            }

            if (stepLabel != null)
            {
                // One-based, because "STEP 0 / 4" is not how anyone counts steps out loud.
                stepLabel.text = run.StepCount == 0
                    ? string.Empty
                    : "STEP " + Mathf.Clamp(run.StepIndex + 1, 1, run.StepCount) + " / " + run.StepCount;
            }

            if (dotsCaption != null)
            {
                dotsCaption.text = step == null ? string.Empty : step.Caption;
            }

            for (int i = 0; i < dotPool.Count; i++)
            {
                bool used = i < run.StepCount;
                dotPool[i].gameObject.SetActive(used);

                if (used)
                {
                    dotPool[i].sprite = i <= run.StepIndex ? dotDone : dotTodo;
                }
            }

            bool hasTip = !string.IsNullOrEmpty(run.Tip);

            if (tipCard != null)
            {
                tipCard.SetActive(hasTip);
            }

            if (tipLabel != null && hasTip)
            {
                tipLabel.text = run.Tip;
            }

            if (cancelHint != null)
            {
                cancelHint.SetActive(run.Cancellable);
            }
        }

        void ApplyBar()
        {
            if (fill != null && track != null)
            {
                // Width rather than a fill amount, so the fill keeps its own rounded ends instead of
                // being clipped square by a radial or horizontal Image fill.
                fill.sizeDelta = new Vector2(track.rect.width * shownProgress, fill.sizeDelta.y);
            }

            if (percentLabel != null)
            {
                percentLabel.text = Mathf.RoundToInt(shownProgress * 100f) + "%";
            }
        }

        void BuildDots(LoadingRun run)
        {
            while (dotPool.Count < run.StepCount)
            {
                Image dot = Instantiate(dotPrefab, dots, false);
                dot.gameObject.SetActive(true);
                dotPool.Add(dot);
            }
        }

        void Detach()
        {
            if (Current != null)
            {
                Current.Changed -= OnRunChanged;
            }
        }

        void Show()
        {
            if (root != null)
            {
                root.SetActive(true);
            }

            if (group != null)
            {
                group.blocksRaycasts = true;
            }

            fadeTarget = 1f;
        }

        void Hide()
        {
            if (group != null)
            {
                group.blocksRaycasts = false;
            }

            fadeTarget = 0f;
        }

        void HideImmediate()
        {
            fadeTarget = 0f;

            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }

            if (root != null)
            {
                root.SetActive(false);
            }
        }

        void Fade()
        {
            if (group == null)
            {
                return;
            }

            if (Mathf.Approximately(group.alpha, fadeTarget))
            {
                // Switched off only once it is actually invisible, or the last frames of the fade
                // would be drawn over whatever came next.
                if (fadeTarget <= 0f && root != null && root.activeSelf)
                {
                    root.SetActive(false);
                }

                return;
            }

            float step = fadeDuration <= 0f ? 1f : Time.unscaledDeltaTime / fadeDuration;
            group.alpha = Mathf.MoveTowards(group.alpha, fadeTarget, step);
        }
    }
}
