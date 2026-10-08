using System;
using Dojo.Framework.UI;
using UnityEngine;

namespace Dojo.Game.Managers
{
    /// <summary>
    /// The authored dialog prefabs, registered with the container and handed to
    /// <see cref="DialogManager"/>.
    /// </summary>
    /// <remarks>
    /// One object rather than four separate registrations, so a prefab that has not been authored
    /// yet can simply be left empty. Registering each dialog type on its own would mean registering
    /// a null, which is not something a container can hand back on request.
    /// <para>
    /// Every field is optional. A dialog with nothing authored builds a plain version of itself,
    /// so the game works with none of these assigned and gets its own look as each is filled in.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class DialogPrefabs
    {
        [Tooltip("Optional. Used instead of a generated confirm dialog.")]
        [SerializeField] ConfirmDialog confirm;

        [Tooltip("Optional. Used instead of a generated prompt dialog.")]
        [SerializeField] PromptDialog prompt;

        [Tooltip("Optional. Used instead of a generated chooser dialog.")]
        [SerializeField] ChooserDialog chooser;

        [Tooltip("Optional. Used instead of a generated save-as dialog.")]
        [SerializeField] SaveAsDialog saveAs;

        [Tooltip("Required for the load-world dialog — unlike the others it builds nothing in " +
                 "code, so without this it has no parts.")]
        [SerializeField] LoadWorldDialog loadWorld;

        [Tooltip("Required for the save-world dialog, for the same reason as the load-world one.")]
        [SerializeField] SaveWorldDialog saveWorld;

        [Tooltip("Required for the shop dialog, for the same reason as the world ones.")]
        [SerializeField] ShopDialog shop;


        /// <summary>The authored confirm dialog, or null to have one generated.</summary>
        public ConfirmDialog Confirm => confirm;

        /// <summary>The authored prompt dialog, or null to have one generated.</summary>
        public PromptDialog Prompt => prompt;

        /// <summary>The authored chooser dialog, or null to have one generated.</summary>
        public ChooserDialog Chooser => chooser;

        /// <summary>The authored save-as dialog, or null to have one generated.</summary>
        public SaveAsDialog SaveAs => saveAs;

        /// <summary>
        /// The authored load-world dialog. Null leaves the dialog unresolvable rather than
        /// generated — see <see cref="LoadWorldDialog"/>.
        /// </summary>
        public LoadWorldDialog LoadWorld => loadWorld;

        /// <summary>
        /// The authored save-world dialog. Null leaves the dialog unresolvable rather than
        /// generated — see <see cref="SaveWorldDialog"/>.
        /// </summary>
        public SaveWorldDialog SaveWorld => saveWorld;

        /// <summary>
        /// The authored shop dialog. Null leaves the dialog unresolvable rather than generated —
        /// see <see cref="ShopDialog"/>.
        /// </summary>
        public ShopDialog Shop => shop;

    }
}
