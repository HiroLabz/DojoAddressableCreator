using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The rollover, hover and press on one of the rail's buttons: a glow behind its icon plate,
    /// the plate growing a little, and its label taking the kit's hover and pressed faces.
    /// </summary>
    /// <remarks>
    /// Rollover: the glow fades up and the plate grows with a small overshoot. Hover: while the
    /// pointer stays, the glow breathes. Press: the glow flares to full and the plate dips, then
    /// back to hover on release - or all the way out if the pointer has left.
    /// <para>
    /// Added to each button by <see cref="InGameEntryPoint"/> at start, which hands over the kit's
    /// sprites: the rail is authored in the scene, not built, so there is no builder to add it.
    /// The glow is the kit's selected plate glow, the one the TAB button already wears when open.
    /// A button that cannot be pressed does none of it. On unscaled time, like the rest of the UI.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RailButtonGlow : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        const float HoverGlow = 0.85f;
        const float BreathLow = 0.6f;
        const float BreathHigh = 1f;
        const float HoverScale = 1.06f;
        const float PressScale = 0.94f;

        /// <summary>The glow's size against its plate: the TAB button's, 132 around 80.</summary>
        const float GlowSpread = 0.325f;

        Button button;
        RectTransform plate;
        Image plateFace;
        Sprite plateIdle;
        Sprite plateLit;
        Image glow;
        Image label;
        Sprite labelIdle;
        Sprite labelHover;
        Sprite labelPressed;
        Color tint;

        bool over;
        bool down;
        int glowTween = -1;
        int scaleTween = -1;

        /// <summary>
        /// Sets the button up: the glow made behind its plate - the child called Plate, or the
        /// button itself when it is one, like TAB - and the label, if it has one, given its faces.
        /// </summary>
        /// <remarks>
        /// A plate wearing the kit's plain face (<paramref name="kitPlate"/>) lights up to
        /// <paramref name="kitPlateLit"/> while hovered or pressed, the way TAB does when open. One
        /// with a face of its own - the shop's pink tile - keeps it and only glows.
        /// </remarks>
        public void Setup(Sprite plateGlow, Sprite kitPlate, Sprite kitPlateLit, Sprite hover, Sprite pressed, Color glowTint)
        {
            button = GetComponent<Button>();
            plate = transform.Find("Plate") as RectTransform;
            if (plate == null)
            {
                plate = (RectTransform)transform;
            }

            plateFace = plate.GetComponent<Image>();
            plateIdle = plateFace != null ? plateFace.sprite : null;
            plateLit = plateIdle != null && plateIdle == kitPlate ? kitPlateLit : null;

            var labelRect = transform.Find("Label");
            label = labelRect != null ? labelRect.GetComponent<Image>() : null;
            labelIdle = label != null ? label.sprite : null;
            labelHover = hover;
            labelPressed = pressed;
            tint = glowTint;

            if (glow == null && plateGlow != null)
            {
                var go = new GameObject("Hover Glow", typeof(RectTransform));
                go.layer = gameObject.layer;
                var rect = (RectTransform)go.transform;
                rect.SetParent(plate, false);

                // First child: over the plate's own face, under its icon - where TAB's glow sits.
                rect.SetAsFirstSibling();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                var spread = plate.rect.width * GlowSpread;
                rect.offsetMin = new Vector2(-spread, -spread);
                rect.offsetMax = new Vector2(spread, spread);

                glow = go.AddComponent<Image>();
                glow.sprite = plateGlow;
                glow.raycastTarget = false;
            }

            Rest();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            over = true;

            if (Pressable && !down)
            {
                ToHover();
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            over = false;

            if (!down)
            {
                ToIdle();
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Pressable || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            down = true;
            SetLabel(labelPressed);
            SetPlate(plateLit);
            GlowTo(1f, 0.08f, LeanTweenType.easeOutCubic);
            ScaleTo(PressScale, 0.08f, LeanTweenType.easeOutCubic);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!down)
            {
                return;
            }

            down = false;

            if (over && Pressable)
            {
                ToHover();
            }
            else
            {
                ToIdle();
            }
        }

        void OnDisable()
        {
            over = false;
            down = false;
            Rest();
        }

        void LateUpdate()
        {
            // Switched off mid-hover - the rail closing under the pointer, or the entry disabled -
            // lets go rather than glowing on at something that cannot be pressed.
            if ((over || down) && !Pressable)
            {
                over = false;
                down = false;
                ToIdle();
            }
        }

        bool Pressable => button == null || button.IsInteractable();

        /// <summary>Rollover, then the breathing that lasts as long as the pointer stays.</summary>
        void ToHover()
        {
            SetLabel(labelHover);
            SetPlate(plateLit);
            ScaleTo(HoverScale, 0.22f, LeanTweenType.easeOutBack);

            Cancel(ref glowTween);
            if (glow == null)
            {
                return;
            }

            glowTween = LeanTween.value(gameObject, glow.color.a, HoverGlow, 0.18f)
                .setEase(LeanTweenType.easeOutCubic)
                .setIgnoreTimeScale(true)
                .setOnUpdate((float a) => SetGlow(a))
                .setOnComplete(Breathe)
                .id;
        }

        void Breathe()
        {
            if (glow == null || !over || down)
            {
                return;
            }

            glowTween = LeanTween.value(gameObject, BreathLow, BreathHigh, 0.9f)
                .setEase(LeanTweenType.easeInOutSine)
                .setLoopPingPong()
                .setIgnoreTimeScale(true)
                .setOnUpdate((float a) => SetGlow(a))
                .id;
        }

        void ToIdle()
        {
            SetLabel(labelIdle);
            SetPlate(plateIdle);
            GlowTo(0f, 0.2f, LeanTweenType.easeOutCubic);
            ScaleTo(1f, 0.2f, LeanTweenType.easeOutCubic);
        }

        /// <summary>Straight back to rest, no tween: for being switched off, when nothing would show one.</summary>
        void Rest()
        {
            Cancel(ref glowTween);
            Cancel(ref scaleTween);
            SetGlow(0f);
            SetLabel(labelIdle);
            SetPlate(plateIdle);

            if (plate != null)
            {
                plate.localScale = Vector3.one;
            }
        }

        void GlowTo(float alpha, float seconds, LeanTweenType ease)
        {
            Cancel(ref glowTween);
            if (glow == null)
            {
                return;
            }

            glowTween = LeanTween.value(gameObject, glow.color.a, alpha, seconds)
                .setEase(ease)
                .setIgnoreTimeScale(true)
                .setOnUpdate((float a) => SetGlow(a))
                .setOnComplete(() => glowTween = -1)
                .id;
        }

        void ScaleTo(float scale, float seconds, LeanTweenType ease)
        {
            Cancel(ref scaleTween);
            if (plate == null)
            {
                return;
            }

            scaleTween = LeanTween.scale(plate, new Vector3(scale, scale, 1f), seconds)
                .setEase(ease)
                .setIgnoreTimeScale(true)
                .setOnComplete(() => scaleTween = -1)
                .id;
        }

        void SetGlow(float alpha)
        {
            if (glow != null)
            {
                glow.color = new Color(tint.r, tint.g, tint.b, alpha);
            }
        }

        void SetPlate(Sprite face)
        {
            if (plateFace != null && face != null)
            {
                plateFace.sprite = face;
            }
        }

        void SetLabel(Sprite face)
        {
            if (label != null && face != null)
            {
                label.sprite = face;
            }
        }

        static void Cancel(ref int tween)
        {
            if (tween >= 0)
            {
                LeanTween.cancel(tween);
                tween = -1;
            }
        }
    }
}
