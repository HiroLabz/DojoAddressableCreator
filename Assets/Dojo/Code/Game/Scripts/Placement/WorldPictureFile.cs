using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Dojo.Game.Placement
{
    /// <summary>
    /// Each world's picture on disk, which the Lobby shows on its continue card and in Recent
    /// Worlds. Written by the game, read back by the Lobby.
    /// </summary>
    /// <remarks>
    /// One file per world, in a <c>WorldPictures</c> folder beside <c>world.json</c> in
    /// <see cref="Application.persistentDataPath"/>, so CLEAR ALL takes them with the saves.
    /// <para>
    /// A world's file is named after the world the way the library matches names - the spaces
    /// around it trimmed and its case ignored - with anything a file name cannot hold replaced, so
    /// "World1" and "world1 " are the one world and one picture.
    /// </para>
    /// <para>
    /// The folder is a constructor argument only so the tests can point it at a temporary one;
    /// the game always uses <see cref="InPersistentData"/>.
    /// </para>
    /// </remarks>
    public sealed class WorldPictureFile
    {
        const string FolderName = "WorldPictures";

        readonly string folder;

        /// <param name="root">The folder the <c>WorldPictures</c> folder lives in.</param>
        public WorldPictureFile(string root)
        {
            folder = Path.Combine(root, FolderName);
        }

        /// <summary>The pictures in <see cref="Application.persistentDataPath"/>, where the game keeps them.</summary>
        public static WorldPictureFile InPersistentData() => new WorldPictureFile(Application.persistentDataPath);

        /// <summary>Where this world's picture is kept, or null for a world with no name.</summary>
        public string PathFor(string world)
        {
            var key = Key(world);
            return key == null ? null : Path.Combine(folder, key + ".png").Replace('\\', '/');
        }

        /// <summary>Writes this world's picture over its last one. A world with no name is skipped.</summary>
        public void Write(string world, byte[] png)
        {
            var path = PathFor(world);

            if (path == null || png == null)
            {
                return;
            }

            Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, png);
        }

        /// <summary>
        /// This world's picture, or null when it has none or the file is not one.
        /// </summary>
        /// <remarks>
        /// A new texture each call, which the caller owns and must destroy.
        /// </remarks>
        public Texture2D Read(string world)
        {
            var path = PathFor(world);

            if (path == null || !File.Exists(path))
            {
                return null;
            }

            byte[] bytes;

            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] The picture at " + path + " could not be read: " + exception.Message);
                return null;
            }

            var picture = new Texture2D(2, 2, TextureFormat.RGB24, false);

            if (picture.LoadImage(bytes))
            {
                return picture;
            }

            UnityEngine.Object.DestroyImmediate(picture);
            Debug.LogWarning("[World] " + path + " is not a picture; the world shows none.");
            return null;
        }

        /// <summary>Removes this world's picture, if it has one.</summary>
        public void Delete(string world)
        {
            var path = PathFor(world);

            if (path == null || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] The picture at " + path + " could not be deleted: " + exception.Message);
            }
        }

        /// <summary>The world's name as a file name, or null when it has none.</summary>
        static string Key(string world)
        {
            var name = (world ?? string.Empty).Trim().ToLowerInvariant();

            if (name.Length == 0)
            {
                return null;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var key = new StringBuilder(name.Length);

            foreach (var c in name)
            {
                key.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            return key.ToString();
        }
    }
}
