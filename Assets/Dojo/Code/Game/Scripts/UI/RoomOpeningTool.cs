using System;
using System.Collections.Generic;
using Dojo.Game.Placement;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Opening = Dojo.Game.Placement.WorldSave.SavedOpening;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The room editor's opening tool: marks where a room can be walked into and out of.
    /// </summary>
    /// <remarks>
    /// ENTRANCE or EXIT on the room bar puts it in hand. With it in hand:
    /// <list type="bullet">
    /// <item>a click on a wall of the floor's edge takes that wall out and makes the gap an opening
    /// of that kind;</item>
    /// <item>a click on a gap already in the walls - marked amber - makes it one without taking
    /// anything out;</item>
    /// <item>a click on an opening of the same kind forgets it; on one of the other kind, it adds
    /// this kind on the same spot, so one door can be both an entrance and an exit;</item>
    /// <item>dragging an opening moves it along the floor's edge, onto the wall or gap nearest the
    /// cursor, taking out the walls where it lands. The place it leaves stays open, as a gap.</item>
    /// </list>
    /// Right click or Esc puts the tool down.
    /// <para>
    /// While a room is being made its openings are drawn on the floor's edge, each kind its own
    /// colour, arrow and label so they cannot be mistaken: an entrance green, its arrow pointing
    /// away from the room, labelled ENTRANCE outside it; an exit violet, its arrow pointing into the
    /// room, labelled EXIT inside it. An entrance and an exit sharing a spot are drawn a little
    /// apart, the entrance outside, so both show; with the tool in hand, the one of its kind is the
    /// one picked. The gaps that are not openings yet are amber. Only the drawing
    /// differs - an opening's saved <c>facing</c> is always the way out, as the room file format
    /// has it. Placement is held off while the tool is in hand, so a click on a wall does not pick
    /// the wall up.
    /// </para>
    /// </remarks>
    public sealed class RoomOpeningTool : MonoBehaviour
    {
        public static readonly Color EntranceColour = new Color(0.3f, 0.9f, 0.45f);
        public static readonly Color ExitColour = new Color(0.72f, 0.48f, 1f);
        static readonly Color GapColour = new Color(1f, 0.75f, 0.2f);
        static readonly Color AimColour = Color.white;
        static readonly Color RefusedColour = new Color(1f, 0.3f, 0.3f);

        /// <summary>How near the cursor must be to an opening or a gap, on the ground, to aim at it.</summary>
        const float AimReach = 1f;

        /// <summary>How far the cursor must move, in pixels, before a press on an opening is a drag.</summary>
        const float DragPixels = 8f;

        /// <summary>How often the gaps are worked out again while a room is being built, in seconds.</summary>
        const float GapRefresh = 0.3f;

        const float ArrowLength = 1f;

        /// <summary>
        /// How far each kind is drawn off the floor's edge - an entrance out, an exit in - so the two
        /// can be told apart where they share a spot.
        /// </summary>
        const float KindOffset = 0.12f;

        /// <summary>How much nearer an opening of the tool's own kind counts, so it is picked where two share a spot.</summary>
        const float OwnKindBias = 0.2f;

        RoomEditor editor;
        PlacementController placement;

        List<Opening> gaps = new List<Opening>();
        bool gapsStale = true;
        float nextGapRefresh;

        Transform markers;
        Material material;
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        int linesUsed;
        readonly List<TextMeshPro> labels = new List<TextMeshPro>();
        int labelsUsed;

        enum Aim { None, Wall, Opening, Gap }

        Aim aim;
        Transform aimedWall;
        int aimedOpening;
        Opening aimedGap;

        // A press on an opening: which one, where the press was, and - once it has become a drag -
        // where the opening would go, and whether it may.
        int pressed = -1;
        Vector2 pressedAt;
        bool dragging;
        Opening dragTarget;
        bool dragAllowed;

        /// <summary><see cref="RoomOpenings.Entrance"/> or <see cref="RoomOpenings.Exit"/> while the tool is in hand, else null.</summary>
        public string Mode { get; private set; }

        /// <summary>Raised with something to tell the player.</summary>
        public event Action<string> Said;

        /// <summary>Raised when the tool is taken up or put down.</summary>
        public event Action ModeChanged;

        public void Init(RoomEditor roomEditor, PlacementController placementController)
        {
            if (editor != null)
            {
                return;
            }

            editor = roomEditor;
            placement = placementController;

            editor.OpeningsChanged += MarkGapsStale;
            editor.Changed += OnEditorChanged;
        }

        void OnDestroy()
        {
            if (editor != null)
            {
                editor.OpeningsChanged -= MarkGapsStale;
                editor.Changed -= OnEditorChanged;
            }

            Stop();

            if (markers != null)
            {
                Destroy(markers.gameObject);
            }

            if (material != null)
            {
                Destroy(material);
            }
        }

        void OnDisable()
        {
            HideMarkers();
        }

        /// <summary>Takes the tool up as <paramref name="kind"/>, or puts it down when it is already that.</summary>
        public void Toggle(string kind)
        {
            if (Mode == kind)
            {
                Stop();
                return;
            }

            if (placement != null && placement.IsBusy && Mode == null)
            {
                Say("Put down what is in hand first.");
                return;
            }

            if (placement != null)
            {
                placement.SuppressGesture = true;
            }

            Mode = kind;
            pressed = -1;
            dragging = false;
            ModeChanged?.Invoke();
        }

        /// <summary>Puts the tool down.</summary>
        public void Stop()
        {
            pressed = -1;
            dragging = false;

            if (Mode == null)
            {
                return;
            }

            Mode = null;

            if (placement != null)
            {
                placement.SuppressGesture = false;
            }

            ModeChanged?.Invoke();
        }

        void MarkGapsStale() => gapsStale = true;

        void OnEditorChanged()
        {
            gapsStale = true;

            if (editor == null || !editor.IsEditing)
            {
                Stop();
                HideMarkers();
            }
        }

        void Update()
        {
            if (editor == null || !editor.IsEditing)
            {
                HideMarkers();
                return;
            }

            // Walls come and go as the room is built, so the gaps are looked for again now and then.
            if (gapsStale || Time.unscaledTime >= nextGapRefresh)
            {
                gaps = editor.Gaps();
                gapsStale = false;
                nextGapRefresh = Time.unscaledTime + GapRefresh;
            }

            aim = Aim.None;

            if (Mode != null && pressed < 0)
            {
                TakeAim();
            }

            if (Mode != null)
            {
                Act();
            }

            Draw();
        }

        // ── Aiming and acting ───────────────────────────────────────────────────────────────

        bool TryGround(out Vector2 at)
        {
            at = Vector2.zero;

            var mouse = Mouse.current;
            var view = Camera.main;

            if (mouse == null || view == null)
            {
                return false;
            }

            float enter;
            var ray = view.ScreenPointToRay(mouse.position.ReadValue());
            var ground = new Plane(Vector3.up, new Vector3(0f, editor.GroundHeight, 0f));

            if (!ground.Raycast(ray, out enter))
            {
                return false;
            }

            var point = ray.GetPoint(enter);
            at = new Vector2(point.x, point.z);
            return true;
        }

        void TakeAim()
        {
            var mouse = Mouse.current;
            var view = Camera.main;

            if (mouse == null || view == null || OverUI())
            {
                return;
            }

            // An opening or gap near the cursor first: they lie on the ground in front of the walls,
            // and an opening has to be pickable to be dragged.
            Vector2 at;

            if (TryGround(out at))
            {
                var best = AimReach;

                for (var i = 0; i < editor.Openings.Count; i++)
                {
                    var distance = Mathf.Min(RoomOpenings.DistanceTo(editor.Openings[i], at),
                        Vector2.Distance(at, ArrowTip(editor.Openings[i])))
                        - (editor.Openings[i].kind == Mode ? OwnKindBias : 0f);

                    if (distance < best)
                    {
                        best = distance;
                        aim = Aim.Opening;
                        aimedOpening = i;
                    }
                }

                if (aim == Aim.Opening)
                {
                    return;
                }
            }

            // Then a wall under the cursor, nearest first.
            var hits = Physics.RaycastAll(view.ScreenPointToRay(mouse.position.ReadValue()), 1000f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                var piece = hit.transform.GetComponentInParent<FurniturePiece>();

                if (piece != null && RoomEditor.IsWall(piece.transform))
                {
                    aim = Aim.Wall;
                    aimedWall = piece.transform;
                    return;
                }
            }

            // Then a gap near the cursor.
            if (TryGround(out at))
            {
                var best = AimReach;

                foreach (var gap in gaps)
                {
                    var distance = RoomOpenings.DistanceTo(gap, at);

                    if (distance < best)
                    {
                        best = distance;
                        aim = Aim.Gap;
                        aimedGap = gap;
                    }
                }
            }
        }

        void Act()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            if (mouse == null)
            {
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame))
            {
                if (pressed >= 0)
                {
                    // A drag called off: the opening stays where it was.
                    pressed = -1;
                    dragging = false;
                    return;
                }

                Stop();
                return;
            }

            if (pressed >= 0)
            {
                FollowPress(mouse);
                return;
            }

            if (!mouse.leftButton.wasPressedThisFrame || OverUI())
            {
                return;
            }

            var made = Mode == RoomOpenings.Entrance ? "an entrance" : "an exit";

            switch (aim)
            {
                case Aim.Wall:
                    string problem;

                    if (editor.CutOpening(aimedWall, Mode, out problem))
                    {
                        Say("Took the wall out: " + made + " on the " + RoomOpenings.SideOf(editor.Openings[editor.Openings.Count - 1].facing.ToVector2()) + " side.");
                    }
                    else
                    {
                        Say(problem);
                    }

                    break;

                case Aim.Opening:
                    // A click or a drag: which, is known once the button comes up or the cursor moves.
                    pressed = aimedOpening;
                    pressedAt = mouse.position.ReadValue();
                    dragging = false;
                    break;

                case Aim.Gap:
                    string refused;

                    Say(editor.MarkGap(aimedGap, Mode, out refused)
                        ? "Marked the gap on the " + RoomOpenings.SideOf(aimedGap.facing.ToVector2()) + " side as " + made + "."
                        : refused);
                    break;

                default:
                    Say("Click a wall on the edge of the floor, or a gap marked amber. Drag an opening to move it.");
                    break;
            }
        }

        /// <summary>Follows a press on an opening: a drag moves it, a click without one changes it.</summary>
        void FollowPress(Mouse mouse)
        {
            if (pressed >= editor.Openings.Count)
            {
                pressed = -1;
                dragging = false;
                return;
            }

            var opening = editor.Openings[pressed];

            if (!dragging && (mouse.position.ReadValue() - pressedAt).magnitude > DragPixels)
            {
                dragging = true;
            }

            if (dragging)
            {
                // The place nearest the cursor, refused or not, so a refusal shows where it was.
                Vector2 at;
                dragTarget = null;
                dragAllowed = TryGround(out at) && editor.TryMoveTarget(pressed, at, out dragTarget);
            }

            if (!mouse.leftButton.wasReleasedThisFrame)
            {
                return;
            }

            var index = pressed;
            pressed = -1;

            if (dragging)
            {
                dragging = false;

                if (dragTarget != null && dragAllowed)
                {
                    editor.MoveOpening(index, dragTarget);
                    Say("Moved the " + opening.kind + " to the " + RoomOpenings.SideOf(dragTarget.facing.ToVector2())
                        + " side. Where it was is left open, marked amber - put a wall back to close it.");
                }
                else
                {
                    Say(dragTarget != null
                        ? "Another opening is there. The " + opening.kind + " stays where it was."
                        : "Drop it on a wall or a gap on the edge of the floor. The " + opening.kind + " stays where it was.");
                }

                return;
            }

            // A click: the same kind forgets it; the other kind joins it on the same spot.
            var made = Mode == RoomOpenings.Entrance ? "an entrance" : "an exit";

            if (opening.kind == Mode)
            {
                editor.RemoveOpening(index);
                Say("No longer " + made + ". The gap is still there, marked amber; put a wall back to close it.");
                return;
            }

            string problem;

            if (editor.AddAt(opening, Mode, out problem))
            {
                Say("This door is an entrance and an exit now. Drag either to move it on its own.");
            }
            else
            {
                Say(problem);
            }
        }

        static bool OverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        void Say(string text) => Said?.Invoke(text);

        // ── Drawing ─────────────────────────────────────────────────────────────────────────

        /// <summary>Colour of an opening's kind.</summary>
        static Color ColourOf(Opening opening) => opening.kind == RoomOpenings.Exit ? ExitColour : EntranceColour;

        /// <summary>The way an opening's arrow points: away from the room for an entrance, into it for an exit.</summary>
        static Vector2 ArrowWay(Opening opening)
        {
            var facing = opening.facing.ToVector2();
            return opening.kind == RoomOpenings.Exit ? -facing : facing;
        }

        static Vector2 ArrowTip(Opening opening) => DrawnCentre(opening) + ArrowWay(opening) * ArrowLength;

        /// <summary>Where an opening is drawn: an entrance just outside the floor's edge, an exit just inside.</summary>
        static Vector2 DrawnCentre(Opening opening)
        {
            var facing = opening.facing.ToVector2();
            return opening.centre.ToVector2() + (opening.kind == RoomOpenings.Exit ? -facing : facing) * KindOffset;
        }

        void Draw()
        {
            linesUsed = 0;
            labelsUsed = 0;

            foreach (var gap in gaps)
            {
                DrawSpan(gap, GapColour, 0.12f);
            }

            for (var i = 0; i < editor.Openings.Count; i++)
            {
                var opening = editor.Openings[i];
                var lifted = dragging && i == pressed;

                // The one being dragged is drawn faded where it was, and in full where it would go.
                DrawOpening(opening, lifted ? Faded(ColourOf(opening)) : ColourOf(opening), !lifted);
            }

            if (dragging && dragTarget != null)
            {
                DrawOpening(dragTarget, dragAllowed ? ColourOf(dragTarget) : RefusedColour, true);
                DrawSpan(dragTarget, dragAllowed ? AimColour : RefusedColour, 0.26f);
            }
            else
            {
                switch (aim)
                {
                    case Aim.Opening:
                        DrawSpan(editor.Openings[aimedOpening], AimColour, 0.26f);
                        break;

                    case Aim.Gap:
                        DrawSpan(aimedGap, AimColour, 0.26f);
                        break;

                    case Aim.Wall:
                        DrawWall(aimedWall);
                        break;
                }
            }

            for (var i = linesUsed; i < lines.Count; i++)
            {
                lines[i].enabled = false;
            }

            for (var i = labelsUsed; i < labels.Count; i++)
            {
                labels[i].gameObject.SetActive(false);
            }
        }

        static Color Faded(Color colour) => new Color(colour.r, colour.g, colour.b, 0.3f);

        void DrawOpening(Opening opening, Color colour, bool labelled)
        {
            var y = editor.GroundHeight + 0.06f;
            var drawn = DrawnCentre(opening);
            var centre = new Vector3(drawn.x, y, drawn.y);
            var along = new Vector3(Mathf.Abs(opening.facing.z), 0f, Mathf.Abs(opening.facing.x)) * (opening.width * 0.5f);

            Line(centre - along, centre + along, colour, 0.2f);
            var way2 = ArrowWay(opening);
            var way = new Vector3(way2.x, 0f, way2.y);
            var tip = centre + way * ArrowLength;
            var side = new Vector3(-way.z, 0f, way.x) * 0.3f;

            Line(centre, tip, colour, 0.2f);
            Line(tip, tip - way * 0.35f + side, colour, 0.2f);
            Line(tip, tip - way * 0.35f - side, colour, 0.2f);

            if (labelled)
            {
                Label(opening.kind == RoomOpenings.Exit ? "EXIT" : "ENTRANCE", tip + way * 0.5f + Vector3.up * 0.3f, colour);
            }
        }

        void DrawSpan(Opening opening, Color colour, float width)
        {
            var y = editor.GroundHeight + 0.06f;
            var centre = new Vector3(opening.centre.x, y, opening.centre.z);
            var along = new Vector3(Mathf.Abs(opening.facing.z), 0f, Mathf.Abs(opening.facing.x)) * (opening.width * 0.5f);

            Line(centre - along, centre + along, colour, width);
        }

        void DrawWall(Transform wall)
        {
            Bounds bounds;

            if (wall == null || !FurniturePlacementService.TryColliderBounds(wall.gameObject, out bounds))
            {
                return;
            }

            var y = editor.GroundHeight + 0.06f;
            var half = bounds.size.x >= bounds.size.z
                ? new Vector3(bounds.extents.x, 0f, 0f)
                : new Vector3(0f, 0f, bounds.extents.z);
            var centre = new Vector3(bounds.center.x, y, bounds.center.z);

            Line(centre - half, centre + half, AimColour, 0.26f);
        }

        Transform Markers()
        {
            if (markers == null)
            {
                // Not under any floor, so a save never takes the marks for part of the room.
                markers = new GameObject("RoomOpeningMarkers").transform;
            }

            return markers;
        }

        void Line(Vector3 from, Vector3 to, Color colour, float width)
        {
            LineRenderer line;

            if (linesUsed < lines.Count)
            {
                line = lines[linesUsed];
            }
            else
            {
                if (material == null)
                {
                    material = new Material(Shader.Find("Sprites/Default"));
                }

                line = new GameObject("Mark").AddComponent<LineRenderer>();
                line.transform.SetParent(Markers(), false);
                line.sharedMaterial = material;
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.numCapVertices = 2;
                line.alignment = LineAlignment.TransformZ;
                line.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                lines.Add(line);
            }

            linesUsed++;
            line.enabled = true;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startColor = colour;
            line.endColor = colour;
            line.startWidth = width;
            line.endWidth = width;
        }

        /// <summary>A word floating at <paramref name="at"/>, turned to the camera so it always reads.</summary>
        void Label(string text, Vector3 at, Color colour)
        {
            TextMeshPro label;

            if (labelsUsed < labels.Count)
            {
                label = labels[labelsUsed];
            }
            else
            {
                label = new GameObject("Label").AddComponent<TextMeshPro>();
                label.transform.SetParent(Markers(), false);
                label.fontSize = 4f;
                label.fontStyle = FontStyles.Bold;
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.rectTransform.sizeDelta = new Vector2(6f, 1.2f);
                label.outlineWidth = 0.2f;
                label.outlineColor = new Color32(0, 0, 0, 200);
                labels.Add(label);
            }

            labelsUsed++;
            label.gameObject.SetActive(true);
            label.text = text;
            label.color = colour;
            label.transform.position = at;

            var view = Camera.main;
            if (view != null)
            {
                label.transform.rotation = view.transform.rotation;
            }
        }

        void HideMarkers()
        {
            foreach (var line in lines)
            {
                if (line != null)
                {
                    line.enabled = false;
                }
            }

            foreach (var label in labels)
            {
                if (label != null)
                {
                    label.gameObject.SetActive(false);
                }
            }
        }
    }
}
