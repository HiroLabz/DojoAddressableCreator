using System;
using System.Collections.Generic;
using Dojo.Framework.UI;
using UnityEngine;
using VContainer;

namespace Dojo.Game.Managers
{
    /// <summary>
    /// Hands out the game's modal dialogs: one is built when a question is actually asked, and torn
    /// down again the moment it is answered.
    /// </summary>
    /// <remarks>
    /// Before this, every caller that wanted a question made its own dialog GameObject — placement
    /// made a confirm, world storage made a chooser and a save-as — so the same modal existed
    /// several times over, each with its own canvas, and none of them knew about the others. That
    /// last part is the real problem: two modals can be up at once, both claiming to be the thing
    /// the player must answer, and the one underneath still accepts clicks through the gap.
    /// <para>
    /// Nothing is created up front. Each dialog is registered against its own interface and reaches
    /// this manager as a <see cref="Func{T}"/> — the container's own factory, so a dialog is still
    /// built and injected by the container, just at the moment it is wanted rather than at startup.
    /// The alternative, holding four live dialogs from the lobby onwards, keeps four canvases and
    /// their whole widget trees in memory through scenes that never ask a single question.
    /// </para>
    /// <para>
    /// The interface is what the caller already names, and that is enough to pick the dialog:
    /// <see cref="OpenDialog{T}"/> looks the maker up by <c>typeof(T)</c>, so there is no id to keep
    /// in step with the set of dialogs — adding one is a registration and nothing else.
    /// </para>
    /// </remarks>
    public sealed class DialogManager : MonoBehaviour, IDialogManager
    {
        /// <summary>
        /// How to build each dialog, keyed by the interface it is asked for by.
        /// </summary>
        /// <remarks>
        /// Keyed by interface rather than by an id, because an id would be a second list to keep in
        /// step with this one, and the only way to get that wrong is to let the two disagree.
        /// </remarks>
        readonly Dictionary<Type, Func<IDialog>> makers = new Dictionary<Type, Func<IDialog>>();

        /// <summary>
        /// The dialogs that exist right now — usually none, and never more than one for as long as
        /// opening a dialog closes what came before.
        /// </summary>
        readonly List<IDialog> live = new List<IDialog>();

        /// <inheritdoc />
        public bool IsAnyOpen
        {
            get
            {
                foreach (var dialog in live)
                {
                    if (Alive(dialog) && dialog.IsOpen)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Receives one factory per dialog from the container.
        /// </summary>
        /// <remarks>
        /// A <see cref="Func{T}"/> rather than the dialog itself, which is the whole point: taking
        /// <c>IConfirmDialog</c> here would have the container build a confirm dialog to satisfy the
        /// argument, at the moment this manager is created, whether or not anything ever asks a
        /// question. Taking the factory defers that to <see cref="OpenDialog{T}"/> while keeping the
        /// dependency visible in the signature and the construction inside the container.
        /// <para>
        /// Injected rather than serialized on this component, because the manager has no scene
        /// object to author against: the container makes it on a bare GameObject, so there would be
        /// nothing for anyone to drag a reference onto.
        /// </para>
        /// </remarks>
        [Inject]
        public void Construct(
            Func<IConfirmDialog> confirm,
            Func<IPromptDialog> prompt,
            Func<IChooserDialog> chooser,
            Func<ISaveAsDialog> saveAs,
            Func<ILoadWorldDialog> loadWorld,
            Func<ISaveWorldDialog> saveWorld,
            Func<IShopDialog> shop)
        {
            Learn(confirm);
            Learn(prompt);
            Learn(chooser);
            Learn(saveAs);
            Learn(loadWorld);
            Learn(saveWorld);
            Learn(shop);
        }

        void Awake()
        {
            // Whether it was made by the container or found by hand, it survives the scene change.
            Persist();

            // Update only exists to notice a dialog closing, so it stays off until there is one.
            enabled = live.Count > 0;
        }

        /// <summary>
        /// Marks this as surviving scene loads. Skipped outside play mode, where Unity refuses the
        /// call outright — there are no scene loads to survive there, so there is nothing to do.
        /// </summary>
        void Persist()
        {
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        /// <summary>
        /// Watches for a dialog that has closed itself, and takes it out of the hierarchy.
        /// </summary>
        /// <remarks>
        /// Polled rather than driven by an event on the dialog, for two reasons. A dialog closes
        /// from inside its own button handler, which then runs the caller's callback — destroying it
        /// there would be pulling the object out from under the code still running on it. And a
        /// dialog handed out by <see cref="OpenDialog{T}"/> but never shown would otherwise linger
        /// forever, having no closing to raise an event about.
        /// <para>
        /// This runs only while something is on screen: the component switches itself off as soon as
        /// the last dialog is gone.
        /// </para>
        /// </remarks>
        void Update()
        {
            for (var i = live.Count - 1; i >= 0; i--)
            {
                var dialog = live[i];

                if (!Alive(dialog) || !dialog.IsOpen)
                {
                    Retire(i);
                }
            }

            if (live.Count == 0)
            {
                enabled = false;
            }
        }

        /// <inheritdoc />
        /// <remarks>The built dialog is parented under this manager, so the set travels with it.</remarks>
        public T OpenDialog<T>() where T : class, IDialog
        {
            Func<IDialog> make;

            if (!makers.TryGetValue(typeof(T), out make))
            {
                Debug.LogError("[Dialogs] Nothing is registered for " + typeof(T).Name + ", so that "
                    + "question cannot be asked. Register one with the LifetimeScope that supplies "
                    + "this manager.", this);

                return null;
            }

            // Only ever one question on screen, and the old one goes before the new one is built so
            // there is never a moment paying for two.
            CloseAll();

            var dialog = make() as T;

            if (dialog == null)
            {
                Debug.LogError("[Dialogs] The registration for " + typeof(T).Name + " produced "
                    + "nothing usable.", this);

                return null;
            }

            Adopt(dialog);

            return dialog;
        }

        /// <inheritdoc />
        public void Confirm(string question, Action onConfirm, Action onCancel = null)
        {
            var dialog = OpenDialog<IConfirmDialog>();

            if (dialog != null)
            {
                dialog.Ask(question, onConfirm, onCancel);
            }
        }

        /// <inheritdoc />
        public void Prompt(string question, string suggestion, Action<string> onConfirm, Action onCancel = null)
        {
            var dialog = OpenDialog<IPromptDialog>();

            if (dialog != null)
            {
                dialog.Ask(question, suggestion, onConfirm, onCancel);
            }
        }

        /// <inheritdoc />
        public void Choose(string heading, IList<string> options, Action<string> onConfirm, Action onCancel = null)
        {
            var dialog = OpenDialog<IChooserDialog>();

            if (dialog != null)
            {
                dialog.Choose(heading, options, onConfirm, onCancel);
            }
        }

        /// <inheritdoc />
        public void SaveAs(string heading, IList<string> existing, Action<string> onConfirm, Action onCancel = null)
        {
            var dialog = OpenDialog<ISaveAsDialog>();

            if (dialog != null)
            {
                dialog.Ask(heading, existing, onConfirm, onCancel);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// Through <see cref="IDialog.Hide"/> rather than each dialog's own Close, and the
        /// difference matters: Close dismisses the question and tells whoever asked it that the
        /// player declined. Nobody declined anything here — the question is simply being replaced
        /// by another — so the waiting caller is dropped silently instead of being handed a cancel
        /// it never earned.
        /// </remarks>
        public void CloseAll()
        {
            for (var i = live.Count - 1; i >= 0; i--)
            {
                var dialog = live[i];

                if (Alive(dialog) && dialog.IsOpen)
                {
                    dialog.Hide();
                }

                Retire(i);
            }

            enabled = false;
        }

        /// <summary>
        /// Whether the object behind a dialog reference is still there.
        /// </summary>
        /// <remarks>
        /// Through the component, not the interface. Comparing an interface reference against null
        /// is a plain reference check, which a destroyed MonoBehaviour passes — Unity's null is a
        /// managed object that only reports itself missing through its own operator, and that
        /// operator is not reached unless the static type is a UnityEngine.Object.
        /// </remarks>
        static bool Alive(IDialog dialog)
        {
            var component = dialog as Component;

            return component != null;
        }

        /// <summary>Remembers how to build one dialog, under the interface it is asked for by.</summary>
        void Learn<T>(Func<T> maker) where T : class, IDialog
        {
            if (maker == null)
            {
                return;
            }

            makers[typeof(T)] = () => maker();
        }

        /// <summary>
        /// Keeps a freshly built dialog with the manager: parented here so it travels with it, and
        /// remembered so it can be closed and cleaned up with the rest.
        /// </summary>
        void Adopt(IDialog dialog)
        {
            var component = dialog as Component;

            if (component != null)
            {
                component.transform.SetParent(transform, false);
            }

            live.Add(dialog);

            // Something is on screen now, so start watching for it to close.
            enabled = true;
        }

        /// <summary>
        /// Forgets a dialog and destroys the object it was built on.
        /// </summary>
        /// <remarks>
        /// It is dropped from the list before <c>Destroy</c>, which Unity defers to the end of the
        /// frame: an answer that immediately asks another question is still running on this object
        /// when it is retired, and would find itself standing on a hole otherwise.
        /// </remarks>
        void Retire(int index)
        {
            var dialog = live[index];
            live.RemoveAt(index);

            var component = dialog as Component;

            if (component != null)
            {
                Discard(component.gameObject);
            }
        }

        /// <summary>
        /// Destroys a dialog's object, the deferred way in play mode and the immediate way outside
        /// it, where <c>Destroy</c> is refused and would leave the object standing.
        /// </summary>
        static void Discard(GameObject dialog)
        {
            if (Application.isPlaying)
            {
                Destroy(dialog);

                return;
            }

            DestroyImmediate(dialog);
        }
    }
}
