using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>What a model in the MVP kit becomes when its prefab is built.</summary>
    public enum KitPieceKind
    {
        /// <summary>Worked out from the model: its name, and the kit's own lists.</summary>
        Auto,

        /// <summary>Anything placed on the floor or on another piece.</summary>
        Furniture,

        /// <summary>Ground: a floor tile or platform. Other pieces stand on it.</summary>
        Floor,

        /// <summary>A wall: rooms are fenced by it, cut it for doorways, and it stands on the floor only.</summary>
        Wall,

        /// <summary>A chair agents sit on: gets a seat facing away from its back.</summary>
        Chair,

        /// <summary>An elevator between floors.</summary>
        Elevator,

        /// <summary>Not built: a file holding several pieces, a work in progress.</summary>
        Skip,
    }

    /// <summary>One model's settings.</summary>
    [Serializable]
    public sealed class KitPiece
    {
        [Tooltip("The model's file name in Assets/Dojo/MVP, without .fbx.")]
        public string model;

        [Tooltip("The prefab's name and inventory address (default_...). Empty: default_ and the model name in snake case.")]
        public string prefabName;

        public KitPieceKind kind = KitPieceKind.Auto;

        [Tooltip("The inventory tab it goes under. Empty: the Addressable Generator's guess from the name.")]
        public string category;

        [Tooltip("A clip in the model to loop, by name (e.g. SuperComputersphere|SuperComputersphereAction). " +
                 "Empty: the piece is built static and its clips are not used.")]
        public string animation;
    }

    /// <summary>
    /// The MVP kit's settings, per model: its prefab name, what kind of piece it is and its inventory
    /// tab. Edited in Tools ▸ Dojo ▸ Content ▸ Builder (Assets), or here in the Inspector.
    /// </summary>
    /// <remarks>
    /// Kept in an asset beside the models, not in code, so the person making the models can say what
    /// each one is without a programmer.
    /// </remarks>
    public sealed class MvpKitSettings : ScriptableObject
    {
        public const string AssetPath = MvpKitImporter.ModelFolder + "/MvpKitSettings.asset";

        /// <summary>The inventory tabs, as the game names them.</summary>
        public static readonly string[] Categories =
            { "", "floors", "walls", "tables", "furnitures", "drawers", "appliances", "others" };

        public List<KitPiece> pieces = new List<KitPiece>();

        /// <summary>The settings asset, made with the kit's current choices when there is none yet.</summary>
        public static MvpKitSettings Load(bool create = true)
        {
            var settings = AssetDatabase.LoadAssetAtPath<MvpKitSettings>(AssetPath);

            if (settings == null && create)
            {
                settings = CreateInstance<MvpKitSettings>();
                AssetDatabase.CreateAsset(settings, AssetPath);
                AssetDatabase.SaveAssets();
            }

            return settings;
        }

        public KitPiece Find(string model)
        {
            foreach (var piece in pieces)
            {
                if (piece != null && string.Equals(piece.model, model, StringComparison.OrdinalIgnoreCase))
                {
                    return piece;
                }
            }

            return null;
        }

        /// <summary>The settings for <paramref name="model"/>, added when it has none.</summary>
        public KitPiece For(string model)
        {
            var piece = Find(model);

            if (piece == null)
            {
                piece = new KitPiece { model = model };
                pieces.Add(piece);
                pieces.Sort((a, b) => string.Compare(a.model, b.model, StringComparison.OrdinalIgnoreCase));
                EditorUtility.SetDirty(this);
            }

            return piece;
        }
    }
}
