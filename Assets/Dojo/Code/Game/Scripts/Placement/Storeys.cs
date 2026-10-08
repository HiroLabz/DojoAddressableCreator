using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// The world's floors: how many there are, which one the player is looking at, and the container
    /// each one's contents live under.
    /// </summary>
    /// <remarks>
    /// World &gt; Floor &gt; 3D objects. Each floor is a <see cref="StoreyFloor"/> under the
    /// <see cref="WorldRoot"/>, standing <see cref="Spacing"/> above the one below it, and
    /// everything on a floor is its child. So which floor a thing is on is simply which container it
    /// is under, and the floor maths for a bare point - a cursor, a probe - is the grid's.
    /// <para>
    /// <b>Viewing a floor hides the ones above it.</b> Their renderers are switched off rather than
    /// their objects, so the agents up there keep working out of sight. Floors below stay drawn.
    /// </para>
    /// <para>
    /// <b>An elevator ride fades the floors it moves between.</b> Switching floors is otherwise
    /// instant. For a ride, <see cref="FadeAway"/> turns the floor it leaves into a fading hologram over
    /// <see cref="FadeSeconds"/>, and it then stays out of sight - whichever floor is viewed - until
    /// <see cref="BringBack"/>; <see cref="FadeIn"/> brings in the floor it arrives on when that was
    /// out of sight above. Only renderers drawn with the <c>Dojo/Fade Lit</c> shader can fade;
    /// anything else on the floor - text, particles, lines - is out of sight for the whole fade.
    /// </para>
    /// <para>
    /// An ordinary object rather than a component, for the same reason <see cref="WorldService"/>
    /// is one: it orchestrates transforms other things own, and needs nothing a GameObject gives.
    /// </para>
    /// </remarks>
    public sealed class Storeys
    {
        /// <summary>
        /// How long a floor takes to fade away or in for an elevator ride: half for the sweep out
        /// from the elevator, half for the hologram to fade.
        /// </summary>
        public const float FadeSeconds = 1.4f;

        /// <summary>How wide the glowing edge of the sweep is, in metres.</summary>
        const float SweepEdge = 1f;

        /// <summary>The bit of a renderer's shader user value that marks its floor as arriving.</summary>
        const uint ArrivingBit = 256u;

        /// <summary>The shader that can fade, by name: pack materials bring their own copy of it.</summary>
        const string FadeShader = "Dojo/Fade Lit";

        /// <summary>Where the sweep starts and how far it goes: elevator x, z, reach, edge width.</summary>
        static readonly int SweepId = Shader.PropertyToID("_DojoSweep");

        readonly WorldRoot world;
        readonly PlacementGrid grid;
        readonly WorldSettings settings;
        readonly List<StoreyFloor> floors = new List<StoreyFloor>();

        /// <summary>
        /// The floor an elevator ride is leaving: fading away, then kept out of sight until the
        /// ride has arrived.
        /// </summary>
        readonly Fade away = new Fade();

        /// <summary>
        /// The floor an elevator ride arrives on when it was out of sight above: fading in, then
        /// drawn as usual.
        /// </summary>
        readonly Fade arriving = new Fade();

        /// <summary>One floor's fade for a ride.</summary>
        sealed class Fade
        {
            /// <summary>The floor, or null when there is none.</summary>
            public StoreyFloor floor;

            /// <summary>How far it has faded: 0 shown .. 1 gone.</summary>
            public float amount;

            /// <summary>The fade under way, or -1 once it has finished.</summary>
            public int tween = -1;

            /// <summary>Its renderers that can fade, as of the last look at the floor.</summary>
            public readonly List<Renderer> renderers = new List<Renderer>();

            /// <summary>Fading in rather than away: the shader runs the sweep the other way.</summary>
            public bool arrives;

            public bool Running => floor != null && tween >= 0;

            /// <summary>What a renderer's shader is handed: how far faded, and which way.</summary>
            public uint Value(uint faded) => arrives && faded > 0u ? faded | ArrivingBit : faded;

            public bool Is(int index) => floor != null && floor.Index == index;

            public void Stop()
            {
                if (tween >= 0)
                {
                    LeanTween.cancel(tween);
                    tween = -1;
                }

                renderers.Clear();
            }
        }

        /// <summary>Whether each shader met so far is the fading one, so a name is read once per shader.</summary>
        readonly Dictionary<Shader, bool> fadingShaders = new Dictionary<Shader, bool>();

        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<Material> materials = new List<Material>();

        public Storeys(WorldRoot world, PlacementGrid grid, WorldSettings settings)
        {
            this.world = world;
            this.grid = grid;
            this.settings = settings;
        }

        /// <summary>A floor was added or removed, or the player switched to another one.</summary>
        public event Action Changed;

        /// <summary>How many floors the world has. Always at least one once a world is built.</summary>
        public int Count
        {
            get
            {
                Collect();
                return floors.Count;
            }
        }

        /// <summary>The floor the player is looking at, from 0.</summary>
        public int Viewed { get; private set; }

        /// <summary>The most floors a world may have. 0 means there is no limit.</summary>
        public int Limit => settings != null ? settings.MaxFloors : 0;

        /// <summary>Whether another floor may be added.</summary>
        public bool CanAdd => Limit <= 0 || Count < Limit;

        /// <summary>Height between one floor and the next, in metres.</summary>
        public float Spacing => grid.FloorSpacing;

        /// <summary>World height of a floor's own level.</summary>
        public float BaseOf(int storey) => grid.StoreyBase(storey);

        /// <summary>World height of the floor the player is looking at.</summary>
        public float ViewedBase => BaseOf(Viewed);

        /// <summary>Which floor a world height is on. Not clamped to the floors that exist.</summary>
        public int StoreyAt(float worldY) => grid.StoreyAt(worldY);

        /// <summary>
        /// Which floor a thing is on: the floor it is parented under, or 0 for scenery that is under
        /// none, which stands on the ground.
        /// </summary>
        public static int StoreyOf(Transform thing)
        {
            var floor = thing != null ? thing.GetComponentInParent<StoreyFloor>() : null;

            return floor != null ? floor.Index : 0;
        }

        /// <summary>
        /// Which floor a click lands on, given everything its ray met: the floor of the nearest
        /// thing on screen, or -1 when the ray met nothing that is.
        /// </summary>
        /// <remarks>
        /// On screen means the floor being viewed or one below it, which show through where the
        /// viewed floor has no tiles. The floors above are hidden but their colliders are still in
        /// the ray's way, so they are passed through rather than taken as what was clicked.
        /// </remarks>
        /// <param name="distances">How far along the ray each hit is.</param>
        /// <param name="floors">The floor of each hit, in the same order.</param>
        /// <param name="viewed">The floor being looked at.</param>
        public static int FloorSeen(IList<float> distances, IList<int> floors, int viewed)
        {
            var seen = -1;
            var nearest = float.MaxValue;

            for (var i = 0; i < distances.Count; i++)
            {
                if (floors[i] < 0 || floors[i] > viewed || distances[i] >= nearest)
                {
                    continue;
                }

                nearest = distances[i];
                seen = floors[i];
            }

            return seen;
        }

        /// <summary>The container of one floor, or the top one when there is no such floor.</summary>
        public Transform Floor(int index)
        {
            Collect();

            return floors[Mathf.Clamp(index, 0, floors.Count - 1)].transform;
        }

        /// <summary>
        /// The container of the floor at this height: where something put down there belongs.
        /// </summary>
        /// <remarks>
        /// Clamped to the floors that exist, so a height a little above the top floor - the top of
        /// a tall cupboard, a model's pivot - still lands on it rather than on a floor nobody added.
        /// </remarks>
        public Transform FloorAt(float worldY) => Floor(StoreyAt(worldY));

        /// <summary>
        /// A position relative to its floor: the height above that floor's own level. What a save
        /// records.
        /// </summary>
        public Vector3 ToFloor(int storey, Vector3 world) => new Vector3(world.x, world.y - BaseOf(storey), world.z);

        /// <summary>The inverse of <see cref="ToFloor"/>.</summary>
        public Vector3 FromFloor(int storey, Vector3 local) => new Vector3(local.x, local.y + BaseOf(storey), local.z);

        /// <summary>Every floor, ground first.</summary>
        public IReadOnlyList<StoreyFloor> All
        {
            get
            {
                Collect();
                return floors;
            }
        }

        /// <summary>
        /// Every thing standing on every floor: each floor's direct children, ground floor first.
        /// </summary>
        /// <remarks>
        /// What used to be "every child of the world root". Copied into a list first, so a caller
        /// may destroy or re-parent what it is given without breaking the walk.
        /// </remarks>
        public List<Transform> Placed()
        {
            Collect();

            var placed = new List<Transform>();

            foreach (var floor in floors)
            {
                foreach (Transform child in floor.transform)
                {
                    placed.Add(child);
                }
            }

            return placed;
        }

        /// <summary>
        /// Makes exactly this many empty floors, viewing the ground one. For a world about to be
        /// built: whatever was standing has already been taken down.
        /// </summary>
        public void Reset(int count)
        {
            Collect();

            foreach (var floor in floors)
            {
                if (floor != null)
                {
                    UnityEngine.Object.DestroyImmediate(floor.gameObject);
                }
            }

            floors.Clear();
            away.Stop();
            away.floor = null;
            arriving.Stop();
            arriving.floor = null;

            for (var i = 0; i < Mathf.Max(1, count); i++)
            {
                floors.Add(Create(i));
            }

            Viewed = 0;
            Apply();
        }

        /// <summary>
        /// Adds an empty floor on top and moves the view to it. Returns its index, or -1 at the limit.
        /// </summary>
        public int Add()
        {
            if (!CanAdd)
            {
                return -1;
            }

            Collect();

            var index = floors.Count;
            floors.Add(Create(index));

            Viewed = index;
            Apply();

            return index;
        }

        /// <summary>Looks at another floor: hides the ones above it and shows the rest, at once.</summary>
        public void View(int index)
        {
            Collect();

            var clamped = Mathf.Clamp(index, 0, Mathf.Max(0, floors.Count - 1));

            if (clamped == Viewed)
            {
                return;
            }

            Viewed = clamped;
            Apply();
        }

        /// <summary>
        /// Shows and hides again from scratch, for things that have just appeared on a floor the
        /// player is not looking at, or moved from one floor to another. A fade under way carries on.
        /// </summary>
        public void Refresh()
        {
            ApplyVisibility();
        }

        /// <summary>
        /// Fades a whole floor away, for an elevator ride leaving it: the hologram sweeps out from
        /// <paramref name="elevator"/>, then fades. The floor then stays out of sight, whichever
        /// floor is viewed, until <see cref="BringBack"/>. Outside Play it is gone at once.
        /// </summary>
        public void FadeAway(int index, Vector3 elevator)
        {
            Collect();

            if (index < 0 || index >= floors.Count)
            {
                return;
            }

            // A ride before this one that never arrived gives its floor back first.
            BringBack();

            away.arrives = false;
            SweepFrom(floors[index], elevator);

            // Kept once it has gone: the floor stays out of sight until the ride gives it back.
            Run(away, floors[index], 0f, 1f, () => Paint(away.floor));
        }

        /// <summary>
        /// Fades a whole floor in, for an elevator ride arriving on it from below, where it was out
        /// of sight: a hologram appears, then the solid floor sweeps out from
        /// <paramref name="elevator"/>. Once in, it is drawn as usual. Outside Play it is there at once.
        /// </summary>
        public void FadeIn(int index, Vector3 elevator)
        {
            Collect();

            if (index < 0 || index >= floors.Count)
            {
                return;
            }

            arriving.Stop();

            var previous = arriving.floor;
            arriving.floor = null;
            Paint(previous);

            arriving.arrives = true;
            SweepFrom(floors[index], elevator);

            Run(arriving, floors[index], 1f, 0f, () =>
            {
                var floor = arriving.floor;
                arriving.floor = null;
                Paint(floor);
            });
        }

        /// <summary>Whether a floor is part-way through fading away or in for a ride.</summary>
        public bool IsFading(int index) => (away.Running && away.Is(index)) || (arriving.Running && arriving.Is(index));

        /// <summary>Whether a floor is fading away for a ride, or has gone and not been brought back.</summary>
        public bool IsAway(int index) => away.Is(index);

        /// <summary>
        /// Ends a ride's fade: the floor it left is drawn, or not, by the usual rule again - floors
        /// above the viewed one hidden, the rest shown.
        /// </summary>
        public void BringBack()
        {
            away.Stop();

            var floor = away.floor;
            away.floor = null;

            Paint(floor);
        }

        /// <summary>
        /// Tells the shader where a floor's sweep starts - the elevator - and how far it has to
        /// travel: out to the farthest corner of anything on the floor.
        /// </summary>
        void SweepFrom(StoreyFloor floor, Vector3 elevator)
        {
            var start = new Vector2(elevator.x, elevator.z);
            var reach = 0f;

            floor.GetComponentsInChildren(true, renderers);

            foreach (var renderer in renderers)
            {
                var bounds = renderer.bounds;
                reach = Mathf.Max(reach, Vector2.Distance(start, new Vector2(bounds.min.x, bounds.min.z)));
                reach = Mathf.Max(reach, Vector2.Distance(start, new Vector2(bounds.min.x, bounds.max.z)));
                reach = Mathf.Max(reach, Vector2.Distance(start, new Vector2(bounds.max.x, bounds.min.z)));
                reach = Mathf.Max(reach, Vector2.Distance(start, new Vector2(bounds.max.x, bounds.max.z)));
            }

            renderers.Clear();
            Shader.SetGlobalVector(SweepId, new Vector4(start.x, start.y, reach, SweepEdge));
        }

        /// <summary>
        /// Fades one floor from <paramref name="from"/> to <paramref name="to"/> over
        /// <see cref="FadeSeconds"/> (0 shown .. 1 gone), then calls <paramref name="finished"/>.
        /// Outside Play it goes straight to the end.
        /// </summary>
        void Run(Fade fade, StoreyFloor floor, float from, float to, Action finished)
        {
            fade.Stop();
            fade.floor = floor;

            if (!Application.isPlaying)
            {
                fade.amount = to;
                finished();
                return;
            }

            fade.amount = from;
            fade.tween = LeanTween.value(world.Root.gameObject, from, to, FadeSeconds)
                .setOnUpdate((float amount) =>
                {
                    fade.amount = amount;
                    Tint(fade);
                })
                .setOnComplete(() =>
                {
                    fade.tween = -1;
                    fade.amount = to;
                    finished();
                })
                .id;

            Paint(floor);
        }

        void Apply()
        {
            ApplyVisibility();

            var handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>
        /// Floors above the viewed one draw nothing; the rest draw as normal, except a floor a ride
        /// has faded away.
        /// </summary>
        /// <remarks>
        /// <c>forceRenderingOff</c> rather than disabling the renderer, so code that turns a
        /// renderer on or off for its own reasons - a highlight, a fade - is not fighting this, and
        /// rather than deactivating the floor, which would stop every agent standing on it.
        /// Colliders stay, so the ray filters in placement and clicks are what keep a hidden floor
        /// out of reach.
        /// <para>
        /// Every floor is gone over every time, so whatever has moved between floors since - an
        /// agent off the stairs - is drawn the way its new floor is.
        /// </para>
        /// </remarks>
        void ApplyVisibility()
        {
            foreach (var floor in floors)
            {
                Paint(floor);
            }
        }

        /// <summary>Draws every renderer on a floor the way the floor is now: shown, hidden or part-way.</summary>
        void Paint(StoreyFloor floor)
        {
            if (floor == null)
            {
                return;
            }

            var fade = floor == away.floor ? away : floor == arriving.floor ? arriving : null;
            var fading = fade != null && fade.Running;
            var amount = fade != null ? fade.amount : floor.Index > Viewed ? 1f : 0f;

            if (fading)
            {
                fade.renderers.Clear();
            }

            floor.GetComponentsInChildren(true, renderers);

            foreach (var renderer in renderers)
            {
                var canFade = CanFade(renderer);
                bool off;
                uint faded;

                Look(fading, amount, canFade, out off, out faded);
                renderer.forceRenderingOff = off;

                if (canFade)
                {
                    SetFaded(renderer, fading ? fade.Value(faded) : faded);

                    if (fading)
                    {
                        fade.renderers.Add(renderer);
                    }
                }
            }

            renderers.Clear();
        }

        /// <summary>Hands a fading floor's renderers how far it has got. Every frame of the fade.</summary>
        static void Tint(Fade fade)
        {
            var faded = fade.Value(Quantize(fade.amount));

            foreach (var renderer in fade.renderers)
            {
                if (renderer != null)
                {
                    SetFaded(renderer, faded);
                }
            }
        }

        /// <summary>
        /// How one renderer on a floor is drawn: switched off or not, and how far faded for its shader.
        /// </summary>
        /// <param name="fading">Whether its floor is part-way through a fade.</param>
        /// <param name="amount">How far the floor has faded: 0 shown .. 1 gone.</param>
        /// <param name="canFade">Whether the renderer is drawn with the fading shader.</param>
        /// <param name="off">Not drawn at all.</param>
        /// <param name="faded">For its shader: 0 shown .. 255 gone.</param>
        public static void Look(bool fading, float amount, bool canFade, out bool off, out uint faded)
        {
            if (!fading)
            {
                off = amount >= 1f;
                faded = 0u;
                return;
            }

            // Part-way: what can fade, does. Anything else would blink in the middle of it, so it is
            // kept out of sight for the whole fade: gone as a floor starts to fade away, back once a
            // floor has finished fading in.
            off = !canFade;
            faded = canFade ? Quantize(amount) : 0u;
        }

        /// <summary>A fade from 0 shown .. 1 gone, as the shader reads it: 0 .. 255.</summary>
        public static uint Quantize(float amount) => (uint)Mathf.RoundToInt(Mathf.Clamp01(amount) * 255f);

        /// <summary>Whether a renderer can fade: a mesh, every material of it on the fading shader.</summary>
        bool CanFade(Renderer renderer)
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
            {
                return false;
            }

            renderer.GetSharedMaterials(materials);

            var can = materials.Count > 0;

            foreach (var material in materials)
            {
                if (material == null || !Fades(material.shader))
                {
                    can = false;
                    break;
                }
            }

            materials.Clear();
            return can;
        }

        bool Fades(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            bool fades;

            if (!fadingShaders.TryGetValue(shader, out fades))
            {
                fades = shader.name == FadeShader;
                fadingShaders[shader] = fades;
            }

            return fades;
        }

        static void SetFaded(Renderer renderer, uint faded)
        {
            var mesh = renderer as MeshRenderer;

            if (mesh != null)
            {
                mesh.SetShaderUserValue(faded);
                return;
            }

            var skinned = renderer as SkinnedMeshRenderer;

            if (skinned != null)
            {
                skinned.SetShaderUserValue(faded);
            }
        }

        /// <summary>
        /// Picks up the floors already under the root, in order. Cheap when nothing has changed.
        /// </summary>
        /// <remarks>
        /// Read from the scene rather than trusted from the list, because the world clears itself
        /// with <c>DestroyImmediate</c> and a destroyed floor would otherwise linger here.
        /// <para>
        /// A world always has its ground floor, and anything standing straight under the root -
        /// authored into a scene, or put there before floors existed - is moved onto it. Left where
        /// it was it would be on no floor at all, and quietly missing from every save.
        /// </para>
        /// </remarks>
        void Collect()
        {
            var intact = floors.Count > 0;

            foreach (var floor in floors)
            {
                if (floor == null)
                {
                    intact = false;
                    break;
                }
            }

            if (intact)
            {
                return;
            }

            floors.Clear();
            var strays = new List<Transform>();

            foreach (Transform child in world.Root)
            {
                var floor = child.GetComponent<StoreyFloor>();

                if (floor != null)
                {
                    floors.Add(floor);
                }
                else if (child.GetComponent<NavMeshSurface>() == null)
                {
                    strays.Add(child);
                }
            }

            floors.Sort((a, b) => a.Index.CompareTo(b.Index));

            if (floors.Count == 0 || floors[0].Index != 0)
            {
                floors.Insert(0, Create(0));
            }

            foreach (var stray in strays)
            {
                stray.SetParent(floors[0].transform, true);
            }

            if (Viewed >= floors.Count)
            {
                Viewed = Mathf.Max(0, floors.Count - 1);
            }
        }

        /// <summary>
        /// One floor's container, at its own height, square to the world.
        /// </summary>
        /// <remarks>
        /// Placed in world space rather than relative to the root, so a floor's level is the grid's
        /// floor maths exactly whatever the root's own transform is.
        /// </remarks>
        StoreyFloor Create(int index)
        {
            var holder = new GameObject(NameFor(index));
            holder.layer = world.Root.gameObject.layer;
            holder.transform.SetParent(world.Root, false);
            holder.transform.position = new Vector3(0f, BaseOf(index), 0f);
            holder.transform.rotation = Quaternion.identity;

            var floor = holder.AddComponent<StoreyFloor>();
            floor.Index = index;

            return floor;
        }

        /// <summary>"Floor 1" for index 0: people count floors from one.</summary>
        public static string NameFor(int index) => "Floor " + (index + 1);

        /// <summary>"1F" for index 0, the label the floor switcher shows.</summary>
        public static string LabelFor(int index) => (index + 1) + "F";
    }
}
