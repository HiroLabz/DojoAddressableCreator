using System;
using System.Collections.Generic;
using Dojo.Framework.World;
using Dojo.Game.Placement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The Lobby's Recent Worlds: every saved world, the current one first, each with its picture.
    /// </summary>
    /// <remarks>
    /// A scroll list that grows a row at a time until it would reach <see cref="limit"/> - the
    /// SETTINGS · SIGN OUT line - and then stops and scrolls. The sizes are
    /// <see cref="RecentWorldsLayout"/>'s, so the builder and this agree on them.
    /// <para>
    /// Rows are copies of <see cref="template"/>, made as they are needed and kept for the next
    /// time the list changes. Each is a button: pressing it raises <see cref="WorldChosen"/> with
    /// its world, and the Lobby menu opens that world. Each shows the world's own picture; a world with none yet keeps the
    /// template's icon. The pictures are read from disk, so this owns them and destroys them.
    /// </para>
    /// <para>
    /// Only worlds from the backend are listed, because the source answers only once it is
    /// ready: before that, and without one, the list says there are none.
    /// </para>
    /// </remarks>
    public sealed class RecentWorldsPanel : MonoBehaviour
    {
        [Serializable]
        public sealed class Row
        {
            public GameObject root;
            public TMP_Text name;
            public TMP_Text detail;

            [Tooltip("The world's picture. Off when it has none.")]
            public RawImage picture;

            [Tooltip("Shown in the picture's place when there is none.")]
            public GameObject icon;
        }

        [Tooltip("This panel, resized to fit its rows.")]
        [SerializeField] RectTransform panel;

        [SerializeField] ScrollRect scroll;

        [Tooltip("What the rows are made from. Kept switched off.")]
        [SerializeField] Row template;

        [Tooltip("Shown when there are no worlds to list.")]
        [SerializeField] TMP_Text empty;

        [Tooltip("The panel stops growing above this, and scrolls instead.")]
        [SerializeField] RectTransform limit;

        [Tooltip("Space kept between the panel's bottom and the limit's top.")]
        [SerializeField] float gapAboveLimit = 40f;

        /// <summary>Raised with the world's name when its row is pressed.</summary>
        public event Action<string> WorldChosen;

        /// <summary>Where the pictures are read from: the game's own folder, unless a test says otherwise.</summary>
        /// <remarks>
        /// Made on first use rather than as the field is built: Unity will not hand out
        /// <see cref="Application.persistentDataPath"/> while a component is being constructed.
        /// </remarks>
        public WorldPictureFile Pictures
        {
            get => pictureFile ?? (pictureFile = WorldPictureFile.InPersistentData());
            set => pictureFile = value;
        }

        WorldPictureFile pictureFile;

        readonly List<Row> rows = new List<Row>();

        /// <summary>The worlds the rows show, in their order - what a pressed row is looked up in.</summary>
        readonly List<string> listed = new List<string>();

        readonly List<Texture2D> pictures = new List<Texture2D>();
        readonly Vector3[] corners = new Vector3[4];

        IWorldSource worlds;
        int shown;
        float laidOutFor = -1f;

        [Inject]
        public void Construct(IWorldSource source)
        {
            if (worlds != null)
            {
                worlds.Changed -= Apply;
            }

            worlds = source;

            if (worlds != null)
            {
                worlds.Changed += Apply;
            }

            Apply();
        }

        void Awake()
        {
            if (template != null && template.root != null)
            {
                template.root.SetActive(false);
            }
        }

        void OnEnable() => Apply();

        /// <summary>
        /// Lays out again whenever the room below changes - the canvas settling on the first frame,
        /// or the window being resized.
        /// </summary>
        void LateUpdate()
        {
            if (!Mathf.Approximately(laidOutFor, Room()))
            {
                LayOut();
            }
        }

        void OnDestroy()
        {
            if (worlds != null)
            {
                worlds.Changed -= Apply;
            }

            ForgetPictures();
        }

        void Apply()
        {
            listed.Clear();
            var current = string.Empty;

            if (worlds != null && worlds.IsReady)
            {
                current = worlds.Current() ?? string.Empty;

                // The open one first, then the rest in the order the source keeps them.
                if (!string.IsNullOrEmpty(current))
                {
                    listed.Add(current);
                }

                foreach (var name in worlds.Names())
                {
                    if (!string.Equals(name, current, StringComparison.OrdinalIgnoreCase))
                    {
                        listed.Add(name);
                    }
                }
            }

            ForgetPictures();
            var file = Pictures;

            for (var i = 0; i < listed.Count; i++)
            {
                var row = RowAt(i);

                if (row == null)
                {
                    break;
                }

                row.root.SetActive(true);

                if (row.name != null)
                {
                    row.name.text = listed[i];
                }

                if (row.detail != null)
                {
                    row.detail.text = string.Equals(listed[i], current, StringComparison.OrdinalIgnoreCase)
                        ? "CURRENT"
                        : string.Empty;
                }

                var picture = file != null ? file.Read(listed[i]) : null;

                if (picture != null)
                {
                    pictures.Add(picture);
                }

                if (row.picture != null)
                {
                    row.picture.texture = picture;
                    row.picture.enabled = picture != null;
                }

                if (row.icon != null)
                {
                    row.icon.SetActive(picture == null);
                }
            }

            for (var i = listed.Count; i < rows.Count; i++)
            {
                rows[i].root.SetActive(false);
            }

            shown = Mathf.Min(listed.Count, rows.Count);

            if (empty != null)
            {
                empty.gameObject.SetActive(listed.Count == 0);
            }

            LayOut();
        }

        /// <summary>The row for position <paramref name="index"/>, made from the template if it is new.</summary>
        Row RowAt(int index)
        {
            while (rows.Count <= index)
            {
                if (template == null || template.root == null)
                {
                    return null;
                }

                var copy = Instantiate(template.root, template.root.transform.parent, false);
                copy.name = "Row" + (rows.Count + 1);

                // By position, looked up when pressed: rows are filled again when the list changes,
                // and a name captured here would open the world the row used to show.
                var position = rows.Count;
                var button = copy.GetComponent<Button>();

                if (button != null)
                {
                    button.onClick.AddListener(() => Choose(position));
                }

                rows.Add(new Row
                {
                    root = copy,
                    name = Twin(copy, template.name),
                    detail = Twin(copy, template.detail),
                    picture = Twin(copy, template.picture),
                    icon = template.icon != null ? Twin(copy, template.icon.transform).gameObject : null,
                });
            }

            return rows[index];
        }

        void Choose(int position)
        {
            if (position < listed.Count)
            {
                WorldChosen?.Invoke(listed[position]);
            }
        }

        /// <summary>The copy's counterpart of one of the template's parts, found by its path.</summary>
        T Twin<T>(GameObject copy, T part) where T : Component
        {
            if (part == null)
            {
                return null;
            }

            var found = copy.transform.Find(PathWithin(template.root.transform, part.transform));
            return found != null ? found.GetComponent<T>() : null;
        }

        static string PathWithin(Transform root, Transform part)
        {
            var path = part.name;

            for (var at = part.parent; at != null && at != root; at = at.parent)
            {
                path = at.name + "/" + path;
            }

            return path;
        }

        /// <summary>Stacks the rows, sizes the list, and grows the panel as far as the room allows.</summary>
        void LayOut()
        {
            var room = Room();
            laidOutFor = room;

            for (var i = 0; i < shown; i++)
            {
                var rect = (RectTransform)rows[i].root.transform;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,
                    -i * (RecentWorldsLayout.RowHeight + RecentWorldsLayout.Gap));
            }

            if (scroll != null && scroll.content != null)
            {
                var content = scroll.content;
                content.sizeDelta = new Vector2(content.sizeDelta.x, RecentWorldsLayout.ListHeight(shown));
                scroll.vertical = RecentWorldsLayout.Scrolls(shown, room);

                if (!scroll.vertical)
                {
                    scroll.verticalNormalizedPosition = 1f;
                }
            }

            if (panel != null)
            {
                panel.sizeDelta = new Vector2(panel.sizeDelta.x, RecentWorldsLayout.PanelHeight(shown, room));
            }
        }

        /// <summary>
        /// The tallest the panel may be: from its top down to <see cref="gapAboveLimit"/> above the
        /// limit. Unbounded with no limit set.
        /// </summary>
        float Room()
        {
            if (panel == null || limit == null || !(panel.parent is RectTransform parent))
            {
                return float.MaxValue;
            }

            panel.GetWorldCorners(corners);
            var top = parent.InverseTransformPoint(corners[1]).y;

            limit.GetWorldCorners(corners);
            var limitTop = parent.InverseTransformPoint(corners[1]).y;

            return top - limitTop - gapAboveLimit;
        }

        void ForgetPictures()
        {
            foreach (var row in rows)
            {
                if (row.picture != null)
                {
                    row.picture.texture = null;
                }
            }

            foreach (var picture in pictures)
            {
                if (picture == null)
                {
                    continue;
                }

                // The editor refuses Destroy outside Play, and the panel is also filled there.
                if (Application.isPlaying)
                {
                    Destroy(picture);
                }
                else
                {
                    DestroyImmediate(picture);
                }
            }

            pictures.Clear();
        }
    }
}
