using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Dojo.Game.Editor
{
    /// <summary>
    /// <c>Tools ▸ Dojo ▸ Clear Addressables…</c>: removes Addressables entries - one pack's, or all
    /// of them - so the Addressable Generator can make a clean set.
    /// </summary>
    /// <remarks>
    /// A pack is its label: the generator gives every entry it makes its pack's name as the only
    /// label, so clearing a pack removes every entry carrying that label, whatever group it is in.
    /// Clear All removes every entry from every group that can be edited.
    /// <para>
    /// Only entries go. The groups stay, because the generator puts entries into existing groups
    /// and skips what it has nowhere to put; the labels stay, because it re-adds them anyway; and
    /// nothing on disk is touched - prefabs, thumbnails, <c>thumbnails.json</c> and
    /// <c>items.json</c> are all where they were, ready for the next run.
    /// </para>
    /// </remarks>
    public sealed class ClearAddressablesWindow : EditorWindow
    {
        const string ProcessFolder = "Assets/Process";

        /// <summary>Entries per pack label, and packs with a Process folder but no entries yet.</summary>
        readonly SortedDictionary<string, int> packs = new SortedDictionary<string, int>();

        int total;
        int unlabelled;
        Vector2 scroll;

        [MenuItem("Tools/Dojo/Clear Addressables...")]
        static void Open()
        {
            var window = GetWindow<ClearAddressablesWindow>(true, "Clear Addressables", true);
            window.minSize = new Vector2(420f, 320f);
            window.Refresh();
        }

        void OnEnable() => Refresh();

        void OnFocus() => Refresh();

        static AddressableAssetSettings Settings => AddressableAssetSettingsDefaultObject.Settings;

        /// <summary>Every group whose entries can be removed - all but the read-only built-in ones.</summary>
        static IEnumerable<AddressableAssetGroup> EditableGroups(AddressableAssetSettings settings)
            => settings.groups.Where(group => group != null && !group.ReadOnly);

        /// <summary>Counts the entries again, per pack.</summary>
        void Refresh()
        {
            packs.Clear();
            total = 0;
            unlabelled = 0;

            // Every pack folder shows, entries or not, so a pack that is already clear says so.
            if (AssetDatabase.IsValidFolder(ProcessFolder))
            {
                foreach (var folder in AssetDatabase.GetSubFolders(ProcessFolder))
                {
                    packs[Path.GetFileName(folder)] = 0;
                }
            }

            var settings = Settings;

            if (settings == null)
            {
                return;
            }

            foreach (var group in EditableGroups(settings))
            {
                foreach (var entry in group.entries)
                {
                    total++;

                    if (entry.labels == null || entry.labels.Count == 0)
                    {
                        unlabelled++;
                        continue;
                    }

                    foreach (var label in entry.labels)
                    {
                        packs.TryGetValue(label, out var count);
                        packs[label] = count + 1;
                    }
                }
            }

            Repaint();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Removes Addressables entries so the Addressable Generator can make a clean set.\n\n"
                + "Groups and labels are kept, since the generator needs the groups. Nothing on disk is "
                + "deleted: prefabs, thumbnails, thumbnails.json and items.json all stay.",
                MessageType.Info);

            var settings = Settings;

            if (settings == null)
            {
                EditorGUILayout.HelpBox("This project has no Addressables settings.", MessageType.Error);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Packs", EditorStyles.boldLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);

            foreach (var pack in packs.ToList())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(pack.Key, EditorStyles.boldLabel, GUILayout.Width(160f));
                    EditorGUILayout.LabelField(pack.Value + (pack.Value == 1 ? " entry" : " entries"));

                    using (new EditorGUI.DisabledScope(pack.Value == 0))
                    {
                        if (GUILayout.Button("Clear", GUILayout.Width(80f)))
                        {
                            ClearPack(pack.Key, pack.Value);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }

            if (unlabelled > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(unlabelled + " entries with no pack label (only Clear All removes them)",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
                {
                    Refresh();
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(total == 0))
                {
                    var previous = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(1f, 0.45f, 0.45f);

                    if (GUILayout.Button("Clear All (" + total + ")", GUILayout.Width(160f), GUILayout.Height(28f)))
                    {
                        ClearAll();
                        GUIUtility.ExitGUI();
                    }

                    GUI.backgroundColor = previous;
                }
            }
        }

        void ClearPack(string pack, int count)
        {
            if (!EditorUtility.DisplayDialog("Clear '" + pack + "'?",
                    "Removes the " + count + " Addressables entries labelled '" + pack + "'.\n\n"
                    + "Run the Addressable Generator on Process/" + pack + " to register them again.",
                    "Clear", "Cancel"))
            {
                return;
            }

            Remove(entry => entry.labels != null && entry.labels.Contains(pack), "'" + pack + "'");
        }

        void ClearAll()
        {
            if (!EditorUtility.DisplayDialog("Clear all Addressables entries?",
                    "Removes all " + total + " entries from every group, every pack included.\n\n"
                    + "Run the Addressable Generator on each pack to register them again.",
                    "Clear All", "Cancel"))
            {
                return;
            }

            Remove(entry => true, "all packs");
        }

        /// <summary>Removes every entry that matches, then saves and redraws.</summary>
        void Remove(System.Func<AddressableAssetEntry, bool> matches, string what)
        {
            var settings = Settings;

            if (settings == null)
            {
                return;
            }

            // Collected first: removing while walking a group's entries changes the collection.
            var doomed = new List<string>();

            foreach (var group in EditableGroups(settings))
            {
                foreach (var entry in group.entries)
                {
                    if (matches(entry))
                    {
                        doomed.Add(entry.guid);
                    }
                }
            }

            var removed = 0;

            foreach (var guid in doomed)
            {
                if (settings.RemoveAssetEntry(guid, false))
                {
                    removed++;
                }
            }

            // One event for the batch instead of one per entry, then saved so the removal survives
            // a restart.
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();

            RepaintAddressablesWindows();
            Refresh();

            Debug.Log("[Clear Addressables] removed " + removed + " entr" + (removed == 1 ? "y" : "ies")
                + " for " + what + ".");
        }

        /// <summary>
        /// The Groups window does not always redraw itself after entries are removed from code, and
        /// keeps showing rows that are gone.
        /// </summary>
        static void RepaintAddressablesWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window != null && window.GetType().Name.Contains("Addressable"))
                {
                    window.Repaint();
                }
            }
        }
    }
}
