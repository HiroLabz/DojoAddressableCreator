using System.Collections.Generic;
using System.IO;
using Dojo.Framework.Content;
using UnityEngine;

namespace Dojo.Game.InGame.Agents
{
    /// <summary>
    /// A roster of credentials on disk: the entries shipped with the game, the areas the player has
    /// painted over them, and where each of those two lives.
    /// </summary>
    /// <remarks>
    /// Two files, one shape. The catalogue under <c>Resources</c> is authored and read-only — a
    /// build cannot write into its own assets — so a painted area is written to
    /// <see cref="Application.persistentDataPath"/> instead and merged over the catalogue on load.
    /// Both use <see cref="AgentRoster"/>, so the saved file is a legible subset of the shipped one
    /// rather than a second format to keep in step.
    /// <para>
    /// Abstract only over <em>where</em>: the agents and the manager keep separate files but want
    /// identical reading, merging and writing, and the manager arrived by asking for "the same as
    /// the agents". A second copy of this would have been a second place for the merge rules to
    /// drift.
    /// </para>
    /// <para>
    /// Touches no scene at all: no GameObject, no Transform, no component. That is what makes it
    /// worth having on its own, and it is the same split <c>WorldLibraryFile</c> already draws
    /// against <c>WorldService</c>.
    /// </para>
    /// </remarks>
    public abstract class CredentialFile
    {
        /// <summary>Content address of the shipped credentials. No extension.</summary>
        public abstract string CatalogueResource { get; }

        /// <summary>Where the shipped catalogue is read from.</summary>
        protected readonly IContentService content;

        /// <summary>What the player's painted areas are called on disk.</summary>
        protected abstract string FileName { get; }

        /// <summary>
        /// What this roster is called in the console. "Agents", "Manager" — enough that a warning
        /// says which of the two files it is about.
        /// </summary>
        protected abstract string Label { get; }

        /// <summary>
        /// Whether the saved file is written with line breaks and indenting.
        /// </summary>
        /// <remarks>
        /// A property rather than a constructor argument, and that is not cosmetic: these are
        /// registered with the container, and VContainer picks the constructor with the
        /// <em>most</em> parameters unless one is marked for injection. A <c>bool</c> overload
        /// alongside the empty one made the container try to resolve <c>System.Boolean</c>, which
        /// nothing registers — it threw, and the aborted build took every other injection in the
        /// scene with it. One constructor only, so there is nothing to choose between.
        /// </remarks>
        public bool PrettyPrint { get; set; }

        /// <summary>
        /// One constructor, taking the content service. Still exactly one — see the remarks on
        /// <see cref="PrettyPrint"/> for why a second overload is not an option here.
        /// </summary>
        protected CredentialFile(IContentService content)
        {
            this.content = content;
            PrettyPrint = true;
        }

        /// <summary>Absolute path the painted areas are read from and written to.</summary>
        public string FullPath
            => Path.Combine(Application.persistentDataPath, FileName).Replace('\\', '/');

        /// <summary>True when the player has saved an area for anybody in this roster.</summary>
        public bool Exists => File.Exists(FullPath);

        /// <summary>
        /// The roster as it should be shown: the shipped credentials with any painted area laid
        /// over the top.
        /// </summary>
        /// <remarks>
        /// The catalogue decides who exists. An id in the saved file that the catalogue no longer
        /// carries is dropped rather than resurrected — somebody removed from the shipped roster
        /// has gone, and a stale saved area should not bring back a card with no credentials behind
        /// it. Only the area travels across, because the rest is not the player's to change.
        /// </remarks>
        public AgentRoster Read()
        {
            var catalogue = ReadCatalogue();
            var saved = ReadSaved();

            if (saved == null || saved.agents == null || catalogue.agents == null)
            {
                return catalogue;
            }

            foreach (var stored in saved.agents)
            {
                if (stored == null)
                {
                    continue;
                }

                var live = catalogue.Find(stored.id);

                if (live == null)
                {
                    Debug.Log("[" + Label + "] " + FullPath + " holds an area for " + stored.id
                        + ", who is no longer in the shipped roster; ignoring it.");
                    continue;
                }

                live.area = stored.area ?? new List<Vector3>();

                // Guarded rather than copied blindly: a zero cell size from a hand-edited file
                // would draw the whole area as nothing at all.
                if (stored.areaCellSize > 0f)
                {
                    live.areaCellSize = stored.areaCellSize;
                }
            }

            return catalogue;
        }

        /// <summary>
        /// The shipped credentials, or an empty roster with a complaint.
        /// </summary>
        /// <remarks>
        /// A failed read is not cached anywhere: it is often transient — the asset not yet resolved
        /// during a domain reload — and a cached empty roster leaves the tab permanently blank with
        /// the one error long since scrolled away.
        /// </remarks>
        public AgentRoster ReadCatalogue()
        {
            TextAsset asset;
            if (content == null || !content.TryGet(CatalogueResource, out asset) || asset == null)
            {
                Debug.LogError("[" + Label + "] No credentials at content address '"
                    + CatalogueResource + "'. That tab will be empty until the file is there.");

                return new AgentRoster();
            }

            var read = JsonUtility.FromJson<AgentRoster>(asset.text);

            // JsonUtility hands back a default-constructed object rather than null when the text is
            // not what it expected, so a non-null roster with a null list is a real possibility and
            // the foreach over it would throw.
            if (read == null || read.agents == null || read.agents.Count == 0)
            {
                Debug.LogError("[" + Label + "] Could not read '" + CatalogueResource
                    + "' as a roster, or it is empty.");

                return new AgentRoster();
            }

            return read;
        }

        /// <summary>The painted areas on disk, or null when nothing has been saved yet.</summary>
        public AgentRoster ReadSaved()
        {
            if (!Exists)
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<AgentRoster>(File.ReadAllText(FullPath));
            }
            catch (IOException error)
            {
                // A painted area is worth losing rather than the whole tab: the catalogue alone
                // still shows every entry, with Define Area offered again.
                Debug.LogWarning("[" + Label + "] Could not read " + FullPath + ": " + error.Message
                    + ". Carrying on with the shipped credentials.");

                return null;
            }
        }

        /// <summary>
        /// Records one entry's area and writes the file.
        /// </summary>
        /// <remarks>
        /// Read-modify-write rather than holding the file open, so the saved roster on disk always
        /// carries everybody's area and not just the one last painted. The file is a handful of
        /// entries and only written when the player commits a paint, so the read costs nothing
        /// worth avoiding.
        /// </remarks>
        public void SaveArea(AgentCredential credential)
        {
            if (credential == null)
            {
                return;
            }

            var roster = ReadSaved() ?? new AgentRoster();
            roster.version = AgentRoster.CurrentVersion;
            roster.Put(credential.Copy());

            Write(roster);
        }

        /// <summary>
        /// Replaces every saved area with the ones these credentials carry.
        /// </summary>
        /// <remarks>
        /// Not read-modify-write, unlike <see cref="SaveArea"/>, and that is the point: this is
        /// what loading a world calls, and a load has to be able to take an area <em>away</em>. A
        /// merge would leave an area painted in some other world standing because the world being
        /// loaded happened not to mention that entry.
        /// <para>
        /// Only credentials with an area are written, so the file stays a short list of what the
        /// player has actually painted rather than a copy of the whole roster.
        /// </para>
        /// </remarks>
        public void SaveAreas(AgentRoster all)
        {
            var saved = new AgentRoster();

            if (all != null && all.agents != null)
            {
                foreach (var credential in all.agents)
                {
                    if (credential != null && credential.HasArea)
                    {
                        saved.Put(credential.Copy());
                    }
                }
            }

            Write(saved);
        }

        /// <summary>Writes the painted areas back over whatever was there.</summary>
        public void Write(AgentRoster roster)
        {
            var path = FullPath;
            var folder = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // No AssetDatabase.Refresh: the file lives outside the project, so it is none of the
            // asset database's business.
            File.WriteAllText(path, JsonUtility.ToJson(roster, PrettyPrint));
        }
    }
}
