using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Records which catalogue entry a piece was built from, so a saved world can be built again.
    /// </summary>
    /// <remarks>
    /// A placed piece is not a prefab instance — <see cref="FurnitureSpawner"/> takes the art
    /// apart, wraps it and bolts placement components onto the wrapper — so Unity's own prefab link
    /// is long gone by the time anything wants to save it. This is the only surviving answer to
    /// "what was this?".
    /// <para>
    /// Both addresses are kept. The GUID is the durable one: it survives renames and folder moves,
    /// which is exactly what a save file written weeks ago needs. The Resources path is the one a
    /// running game can actually load from, and it is only a path — re-running the thumbnail
    /// generator after moving assets is what keeps the two agreeing.
    /// </para>
    /// </remarks>
    public sealed class PlacedPiece : MonoBehaviour
    {
        [Tooltip("Asset GUID of the prefab this was built from. Matches 'prefabId' in thumbnails.json.")]
        [SerializeField] string prefabId;

        [Tooltip("Resources path the prefab loads from. Matches 'prefabResource' in thumbnails.json.")]
        [SerializeField] string resource;

        /// <summary>Asset GUID of the source prefab.</summary>
        public string PrefabId => prefabId;

        /// <summary>Resources path of the source prefab.</summary>
        public string Resource => resource;

        /// <summary>True once it can be found again.</summary>
        public bool IsResolvable => !string.IsNullOrEmpty(prefabId) || !string.IsNullOrEmpty(resource);

        /// <summary>Records where this piece came from. Called once, as it is built.</summary>
        public void Set(string id, string resourcePath)
        {
            prefabId = id;
            resource = resourcePath;
        }
    }
}
