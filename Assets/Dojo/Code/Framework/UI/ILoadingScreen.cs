using System.Collections.Generic;

namespace Dojo.Framework.UI
{
    /// <summary>One step in a load, as the caller plans it before anything starts.</summary>
    /// <remarks>
    /// Two pieces of wording rather than one because the mock shows both, and they say different
    /// things: <see cref="Label"/> is what is happening now, above the bar, and <see cref="Caption"/>
    /// is what will have happened once the step is done, beside the dots. "PLACING PIECES" and
    /// "Pieces placed" are the same step in the present and the past tense.
    /// </remarks>
    public readonly struct LoadingStepPlan
    {
        /// <summary>Shown above the bar while this step runs, e.g. "PLACING PIECES".</summary>
        public readonly string Label;

        /// <summary>Shown beside the dots, e.g. "Pieces placed".</summary>
        public readonly string Caption;

        /// <summary>
        /// How much of the whole bar this step is worth.
        /// </summary>
        /// <remarks>
        /// Equal weights make four steps a quarter each, which is honest only if they really take
        /// about as long. A connection that answers in a moment and a content preload that takes
        /// twenty seconds are not a half each, and a bar that says they are is a bar that appears
        /// to stall.
        /// </remarks>
        public readonly float Weight;

        public LoadingStepPlan(string label, string caption = null, float weight = 1f)
        {
            Label = label ?? string.Empty;
            Caption = caption ?? label ?? string.Empty;
            Weight = weight <= 0f ? 1f : weight;
        }
    }

    /// <summary>Everything that varies between one load and the next.</summary>
    public readonly struct LoadingRequest
    {
        /// <summary>The big line, e.g. "STUDIO FLOOR".</summary>
        public readonly string Title;

        /// <summary>The line under it, e.g. "Building the world from your last save."</summary>
        public readonly string Subtitle;

        /// <summary>The steps, in the order they will run.</summary>
        public readonly IList<LoadingStepPlan> Steps;

        /// <summary>
        /// Whether ESC aborts this load. Controls both the hint in the corner and whether the key
        /// does anything, so the screen never offers a way out that is not there.
        /// </summary>
        public readonly bool Cancellable;

        /// <summary>The tip card's body, or empty to hide the card.</summary>
        public readonly string Tip;

        public LoadingRequest(
            string title,
            string subtitle = null,
            IList<LoadingStepPlan> steps = null,
            bool cancellable = false,
            string tip = null)
        {
            Title = title ?? string.Empty;
            Subtitle = subtitle ?? string.Empty;
            Steps = steps;
            Cancellable = cancellable;
            Tip = tip ?? string.Empty;
        }
    }

    /// <summary>
    /// The one loading screen: a canvas of its own that outlives every scene, put up by whoever is
    /// about to make the player wait.
    /// </summary>
    /// <remarks>
    /// One instance for the whole process, instantiated once and never torn down — which is the
    /// point. A loading screen built per transition cannot cover the transition that destroys it,
    /// and a scene change is exactly when something needs to stay on screen.
    /// <para>
    /// This hands out a <see cref="LoadingRun"/> and then gets out of the way. Everything about a
    /// particular load — its steps, its progress, whether it was cancelled — lives on the run, so
    /// callers pass that around rather than reaching back through the service, and code that reports
    /// progress never has to know there is a screen at all.
    /// </para>
    /// <para>
    /// <b>One run at a time.</b> Beginning a second replaces the first, because there is one screen
    /// and it can only say one thing. Work that should not take the screen over belongs in a step's
    /// job — see <see cref="LoadingStep.AddJob"/> — not in a run of its own.
    /// </para>
    /// </remarks>
    public interface ILoadingScreen
    {
        /// <summary>Whether the screen is on, which includes fading in or out.</summary>
        bool IsShowing { get; }

        /// <summary>The run currently on screen, or null.</summary>
        LoadingRun Current { get; }

        /// <summary>
        /// Puts the screen up and hands back the run to report progress on.
        /// </summary>
        /// <remarks>
        /// The run is <see cref="System.IDisposable"/>, so a <c>using</c> block is the tidiest way
        /// to guarantee the screen comes down even if the work between throws.
        /// </remarks>
        LoadingRun Begin(LoadingRequest request);
    }
}
