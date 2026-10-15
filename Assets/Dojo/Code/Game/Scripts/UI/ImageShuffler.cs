using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI
{
    /// <summary>
    /// Shows a set of pictures one at a time in an <see cref="Image"/>, in shuffled order: each is
    /// held for a few seconds, then fades out as the next fades in, round and round for as long as
    /// the image is on screen.
    /// </summary>
    /// <remarks>
    /// Shuffled a round at a time, like a deck: every picture is shown once before any is shown
    /// again, and a new round never opens on the picture the last one closed on.
    /// <para>
    /// The fade is a crossfade through a twin image laid exactly over this one. Both move at once -
    /// this one out, the twin in - because the pictures are cut-outs: fading only the top one in
    /// would leave the old picture showing through its transparent parts.
    /// </para>
    /// <para>
    /// The pictures are listed on the component rather than found at run time, since a build has
    /// no folders to look in. <c>Fill From Folder</c> in its menu lists every sprite in
    /// <see cref="folder"/> again, for when pictures are added or removed. Adding the component
    /// fills it too.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Image))]
    public sealed class ImageShuffler : MonoBehaviour
    {
        [SerializeField] Sprite[] sprites = new Sprite[0];

        [Tooltip("Seconds each picture stays before the next fades in.")]
        [SerializeField] float holdSeconds = 3f;

        [Tooltip("Seconds the crossfade from one picture to the next takes.")]
        [SerializeField] float fadeSeconds = 0.6f;

        [Tooltip("Where Fill From Folder looks for the pictures.")]
        [SerializeField] string folder = "Assets/Dojo/Art/2D/UI/hiro";

        readonly Queue<int> deck = new Queue<int>();
        readonly System.Random random = new System.Random();

        Image image;
        Image twin;
        Color rest;
        int shown = -1;
        float nextAt;
        int fade = -1;

        void Awake()
        {
            image = GetComponent<Image>();
            rest = image.color;

            // Never stretched: the pictures are not all the same shape as the frame they sit in.
            image.preserveAspect = true;
        }

        void OnEnable()
        {
            if (sprites.Length == 0)
            {
                return;
            }

            // The first picture straight away, no fade: there is nothing to fade from.
            shown = Draw();
            image.sprite = sprites[shown];
            image.color = rest;
            nextAt = Time.unscaledTime + holdSeconds;
        }

        void OnDisable()
        {
            if (fade >= 0)
            {
                LeanTween.cancel(fade);
                fade = -1;
            }

            if (image != null)
            {
                image.color = rest;
            }

            if (twin != null)
            {
                twin.color = Clear(rest);
            }
        }

        void Update()
        {
            if (sprites.Length < 2 || fade >= 0 || Time.unscaledTime < nextAt)
            {
                return;
            }

            CrossfadeTo(Draw());
        }

        void CrossfadeTo(int next)
        {
            var from = image;
            var to = Twin();
            to.sprite = sprites[next];

            fade = LeanTween.value(gameObject, 0f, 1f, fadeSeconds)
                .setEase(LeanTweenType.easeInOutSine)
                .setIgnoreTimeScale(true)
                .setOnUpdate((float t) =>
                {
                    from.color = new Color(rest.r, rest.g, rest.b, rest.a * (1f - t));
                    to.color = new Color(rest.r, rest.g, rest.b, rest.a * t);
                })
                .setOnComplete(() =>
                {
                    // The picture moves down to this image and the twin goes clear again, in the
                    // same frame, so the hand-over cannot be seen.
                    fade = -1;
                    shown = next;
                    image.sprite = sprites[next];
                    image.color = rest;
                    to.color = Clear(rest);
                    nextAt = Time.unscaledTime + holdSeconds;
                })
                .id;
        }

        /// <summary>The next picture off the deck, dealing a new round when it runs out.</summary>
        int Draw()
        {
            if (deck.Count == 0)
            {
                foreach (var index in Deal(sprites.Length, shown, random))
                {
                    deck.Enqueue(index);
                }
            }

            return deck.Dequeue();
        }

        /// <summary>
        /// One round: every index below <paramref name="count"/> once, in random order, never
        /// starting with <paramref name="last"/> - the picture on screen as the round begins.
        /// </summary>
        public static int[] Deal(int count, int last, System.Random random)
        {
            var round = new int[count];
            for (var i = 0; i < count; i++)
            {
                round[i] = i;
            }

            // Fisher-Yates.
            for (var i = count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (round[i], round[j]) = (round[j], round[i]);
            }

            // A repeat across the join would read as the shuffle sticking; swap it further in.
            if (count > 1 && round[0] == last)
            {
                var j = 1 + random.Next(count - 1);
                (round[0], round[j]) = (round[j], round[0]);
            }

            return round;
        }

        /// <summary>The image laid over this one to fade the next picture in on, made the first time it is needed.</summary>
        Image Twin()
        {
            if (twin != null)
            {
                return twin;
            }

            var go = new GameObject(name + " (next)", typeof(RectTransform));
            go.layer = gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            twin = go.AddComponent<Image>();
            twin.preserveAspect = true;
            twin.type = image.type;
            twin.material = image.material;
            twin.raycastTarget = false;
            twin.color = Clear(rest);
            return twin;
        }

        static Color Clear(Color colour) => new Color(colour.r, colour.g, colour.b, 0f);

#if UNITY_EDITOR
        void Reset() => Fill();

        /// <summary>Lists every sprite in <see cref="folder"/>, in number order where the names are numbers.</summary>
        [ContextMenu("Fill From Folder")]
        void Fill()
        {
            var found = new List<Sprite>();

            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Sprite", new[] { folder }))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                {
                    found.Add(sprite);
                }
            }

            found.Sort((a, b) =>
                int.TryParse(a.name, out var x) && int.TryParse(b.name, out var y)
                    ? x.CompareTo(y)
                    : string.CompareOrdinal(a.name, b.name));

            UnityEditor.Undo.RecordObject(this, "Fill From Folder");
            sprites = found.ToArray();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
