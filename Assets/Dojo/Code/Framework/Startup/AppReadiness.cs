using System;
using System.Collections.Generic;

namespace Dojo.Framework.Startup
{
    /// <inheritdoc cref="IAppReadiness"/>
    /// <remarks>
    /// Deliberately plain: a dictionary and an event, no async and no Unity types. Startup order is
    /// decided by whoever drives the preprocesses; this only remembers what they reported. That is
    /// what makes it testable without an editor, and what lets the Lobby UI and the popup both read
    /// the same state without either of them knowing what a preprocess actually does.
    /// </remarks>
    public sealed class AppReadiness : IAppReadiness
    {
        sealed class Entry
        {
            public bool Required;
            public PreprocessState State;
            public string Error;
        }

        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

        public event Action Changed;

        public bool AllRequiredSucceeded
        {
            get
            {
                foreach (var entry in entries.Values)
                {
                    if (entry.Required && entry.State != PreprocessState.Succeeded)
                    {
                        return false;
                    }
                }

                // No required preprocess is outstanding. With none declared at all this is
                // vacuously true, which is correct: declaration happens while the container is
                // built, so by the time any UI can ask, the set is already complete.
                return true;
            }
        }

        public void Declare(string name, bool required)
        {
            if (string.IsNullOrEmpty(name) || entries.ContainsKey(name))
            {
                // Re-declaring is ignored rather than reset. A scene reload re-running registration
                // must not throw away the result of a preprocess that already finished.
                return;
            }

            entries[name] = new Entry { Required = required, State = PreprocessState.Pending };
            Raise();
        }

        public void MarkRunning(string name) => Transition(name, PreprocessState.Running, null);

        public void MarkSucceeded(string name) => Transition(name, PreprocessState.Succeeded, null);

        public void MarkFailed(string name, string error) => Transition(name, PreprocessState.Failed, error);

        public PreprocessState StateOf(string name)
        {
            Entry entry;
            return entries.TryGetValue(name, out entry) ? entry.State : PreprocessState.Pending;
        }

        public bool TryGetError(string name, out string error)
        {
            error = null;

            Entry entry;
            if (!entries.TryGetValue(name, out entry) || entry.State != PreprocessState.Failed)
            {
                return false;
            }

            error = entry.Error;
            return true;
        }

        /// <summary>
        /// Applies a state change and announces it, but only when something actually moved — the
        /// Lobby rebuilds button state on every event, and a preprocess re-reporting the state it
        /// is already in should not cost a UI pass.
        /// </summary>
        /// <remarks>
        /// An undeclared name is ignored. Marking something nobody declared cannot make the gate
        /// more open or more shut, so there is no state to record and nothing to announce.
        /// </remarks>
        void Transition(string name, PreprocessState next, string error)
        {
            Entry entry;
            if (string.IsNullOrEmpty(name) || !entries.TryGetValue(name, out entry))
            {
                return;
            }

            if (entry.State == next && entry.Error == error)
            {
                return;
            }

            entry.State = next;
            entry.Error = error;
            Raise();
        }

        void Raise()
        {
            var handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
