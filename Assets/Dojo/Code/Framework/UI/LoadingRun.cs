using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Dojo.Framework.UI
{
    /// <summary>
    /// One unit of background work inside a step: an addressables download, a connection attempt,
    /// anything that can say how far along it is.
    /// </summary>
    /// <remarks>
    /// Jobs exist so a step can be made of several things running at once without the caller having
    /// to average them by hand. A step with no jobs is reported directly and this never appears; a
    /// step with jobs takes their weighted mean and ignores anything reported to it, because two
    /// sources for one number is two numbers waiting to disagree.
    /// </remarks>
    public sealed class LoadingJob : IDisposable
    {
        readonly LoadingStep step;

        internal LoadingJob(LoadingStep step, string name, float weight)
        {
            this.step = step;
            Name = name ?? string.Empty;
            Weight = weight <= 0f ? 1f : weight;
        }

        /// <summary>What this job is, for diagnostics. Never drawn — the step's label speaks for it.</summary>
        public string Name { get; }

        /// <summary>How much of its step this job is worth.</summary>
        public float Weight { get; }

        /// <summary>0..1.</summary>
        public float Progress { get; private set; }

        /// <summary>True once this job has finished, however it finished.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>Reports how far along this job is. Values outside 0..1 are clamped.</summary>
        public void Report(float normalized)
        {
            if (IsComplete)
            {
                return;
            }

            float next = Mathf.Clamp01(normalized);

            if (Mathf.Approximately(next, Progress))
            {
                return;
            }

            Progress = next;
            step.Touch();
        }

        /// <summary>Marks this job done and its progress full.</summary>
        public void Complete()
        {
            if (IsComplete)
            {
                return;
            }

            IsComplete = true;
            Progress = 1f;
            step.Touch();
        }

        /// <summary>Completing it, so a <c>using</c> block cannot leave a job stuck part-done.</summary>
        public void Dispose()
        {
            Complete();
        }
    }

    /// <summary>One step of a load, as it is actually running.</summary>
    public sealed class LoadingStep
    {
        readonly LoadingRun run;
        readonly List<LoadingJob> jobs = new List<LoadingJob>();

        float reported;

        internal LoadingStep(LoadingRun run, LoadingStepPlan plan, int index)
        {
            this.run = run;
            Label = plan.Label;
            Caption = plan.Caption;
            Weight = plan.Weight;
            Index = index;
        }

        /// <summary>Shown above the bar while this step runs.</summary>
        public string Label { get; private set; }

        /// <summary>Shown beside the dots.</summary>
        public string Caption { get; private set; }

        /// <summary>How much of the whole bar this step is worth.</summary>
        public float Weight { get; }

        /// <summary>Where this step sits in the run, from zero.</summary>
        public int Index { get; }

        /// <summary>True once this step has finished.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>
        /// How far through this step is, 0..1 — the weighted mean of its jobs when it has any, and
        /// whatever was last reported when it has none.
        /// </summary>
        public float Progress
        {
            get
            {
                if (IsComplete)
                {
                    return 1f;
                }

                if (jobs.Count == 0)
                {
                    return reported;
                }

                float total = 0f;
                float done = 0f;

                for (int i = 0; i < jobs.Count; i++)
                {
                    total += jobs[i].Weight;
                    done += jobs[i].Weight * jobs[i].Progress;
                }

                return total <= 0f ? 0f : Mathf.Clamp01(done / total);
            }
        }

        /// <summary>
        /// Reports this step's progress directly. Ignored once the step has jobs, which own the
        /// number from then on.
        /// </summary>
        public void Report(float normalized)
        {
            if (IsComplete || jobs.Count > 0)
            {
                return;
            }

            float next = Mathf.Clamp01(normalized);

            if (Mathf.Approximately(next, reported))
            {
                return;
            }

            reported = next;
            Touch();
        }

        /// <summary>Renames the step mid-flight, for a stage whose wording is only known once it starts.</summary>
        public void Describe(string label, string caption = null)
        {
            Label = label ?? Label;
            Caption = caption ?? Caption;
            Touch();
        }

        /// <summary>
        /// Adds a piece of concurrent work to this step. The step's progress becomes the weighted
        /// mean of its jobs from the first one onwards.
        /// </summary>
        public LoadingJob AddJob(string name, float weight = 1f)
        {
            var job = new LoadingJob(this, name, weight);
            jobs.Add(job);
            Touch();
            return job;
        }

        /// <summary>Marks the step done. Its jobs go with it, finished or not.</summary>
        public void Complete()
        {
            if (IsComplete)
            {
                return;
            }

            IsComplete = true;

            for (int i = 0; i < jobs.Count; i++)
            {
                jobs[i].Complete();
            }

            Touch();
        }

        internal void Touch()
        {
            run.Touch();
        }
    }

    /// <summary>
    /// One load, from the moment the screen goes up to the moment it comes down.
    /// </summary>
    /// <remarks>
    /// Handed out by <see cref="ILoadingScreen.Begin"/> and passed around rather than reached for:
    /// code that reports progress takes a <see cref="LoadingStep"/> or a <see cref="LoadingJob"/>
    /// and never needs to know a screen exists. That is what lets the same content and connection
    /// code run with no screen at all.
    /// </remarks>
    public sealed class LoadingRun : IDisposable
    {
        readonly LoadingStep[] steps;
        readonly CancellationTokenSource cancellation;

        int holds;

        internal LoadingRun(LoadingRequest request)
        {
            Title = request.Title;
            Subtitle = request.Subtitle;
            Tip = request.Tip;
            Cancellable = request.Cancellable;

            int count = request.Steps == null ? 0 : request.Steps.Count;
            steps = new LoadingStep[count];

            for (int i = 0; i < count; i++)
            {
                steps[i] = new LoadingStep(this, request.Steps[i], i);
            }

            // Only a cancellable run owns a source. A token that can never be cancelled is honest
            // about a load that cannot be, and costs nothing to pass down.
            cancellation = request.Cancellable ? new CancellationTokenSource() : null;

            StepIndex = -1;
        }

        /// <summary>Raised whenever anything the screen draws has changed.</summary>
        public event Action Changed;

        /// <summary>The big line.</summary>
        public string Title { get; private set; }

        /// <summary>The line under it.</summary>
        public string Subtitle { get; private set; }

        /// <summary>The small line under the bar, e.g. "34 pieces · 3 agents".</summary>
        public string Detail { get; private set; } = string.Empty;

        /// <summary>The tip card's body, or empty for no card.</summary>
        public string Tip { get; private set; }

        /// <summary>Whether ESC aborts this load.</summary>
        public bool Cancellable { get; }

        /// <summary>The steps, in order.</summary>
        public IReadOnlyList<LoadingStep> Steps => steps;

        /// <summary>How many steps there are. The N in "STEP 2 / 4".</summary>
        public int StepCount => steps.Length;

        /// <summary>Which step is running, from zero, or -1 before the first <see cref="Advance"/>.</summary>
        public int StepIndex { get; private set; }

        /// <summary>The step running now, or null before the first <see cref="Advance"/>.</summary>
        public LoadingStep Current =>
            StepIndex >= 0 && StepIndex < steps.Length ? steps[StepIndex] : null;

        /// <summary>True once the run has finished, however it finished.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>True when the run ended because the player cancelled it.</summary>
        public bool IsCancelled { get; private set; }

        /// <summary>
        /// The token to hand down to the work being waited on. Never cancelled on a run that was
        /// not made cancellable.
        /// </summary>
        public CancellationToken CancellationToken =>
            cancellation == null ? CancellationToken.None : cancellation.Token;

        /// <summary>Whether something is holding the screen up past its last step.</summary>
        public bool IsHeld => holds > 0;

        /// <summary>
        /// How far through the whole run, 0..1, weighted by each step's weight.
        /// </summary>
        /// <remarks>
        /// Monotonic by construction: every step before the current one counts in full whether or
        /// not it was explicitly completed, so a caller that advances past a step without finishing
        /// it cannot make the bar go backwards. A bar that retreats reads as the game undoing work.
        /// </remarks>
        public float Overall
        {
            get
            {
                if (IsComplete)
                {
                    return 1f;
                }

                float total = 0f;

                for (int i = 0; i < steps.Length; i++)
                {
                    total += steps[i].Weight;
                }

                if (total <= 0f)
                {
                    return 0f;
                }

                float done = 0f;

                for (int i = 0; i < steps.Length; i++)
                {
                    if (i < StepIndex)
                    {
                        done += steps[i].Weight;
                    }
                    else if (i == StepIndex)
                    {
                        done += steps[i].Weight * steps[i].Progress;
                    }
                }

                return Mathf.Clamp01(done / total);
            }
        }

        /// <summary>
        /// Moves to the next step, finishing the one before it.
        /// </summary>
        /// <returns>The step now running, or null when there are none left.</returns>
        public LoadingStep Advance()
        {
            if (IsComplete)
            {
                return null;
            }

            if (Current != null)
            {
                Current.Complete();
            }

            if (StepIndex + 1 >= steps.Length)
            {
                return null;
            }

            StepIndex++;
            Touch();

            return Current;
        }

        /// <summary>Moves to the next step and renames it in one call.</summary>
        public LoadingStep Advance(string label, string caption = null)
        {
            LoadingStep step = Advance();

            if (step != null)
            {
                step.Describe(label, caption);
            }

            return step;
        }

        /// <summary>Sets the small line under the bar.</summary>
        public void SetDetail(string detail)
        {
            string next = detail ?? string.Empty;

            if (next == Detail)
            {
                return;
            }

            Detail = next;
            Touch();
        }

        /// <summary>Replaces the title and subtitle mid-run.</summary>
        public void Describe(string title, string subtitle = null)
        {
            Title = title ?? Title;
            Subtitle = subtitle ?? Subtitle;
            Touch();
        }

        /// <summary>
        /// Keeps the screen up past the end of the run, until the returned handle is disposed.
        /// </summary>
        /// <remarks>
        /// For the gap between a load finishing and the thing it loaded being ready to look at — a
        /// scene that is streamed but not yet activated, most obviously. Without this the screen
        /// comes down on the frame the last step completes and the player sees whatever is behind
        /// it, which is usually the old scene.
        /// </remarks>
        public IDisposable Hold()
        {
            holds++;
            Touch();
            return new Holder(this);
        }

        /// <summary>
        /// Aborts the run: cancels the token, then finishes. Does nothing on a run that was not
        /// made cancellable, so a stray ESC cannot stop a load that has no way to stop safely.
        /// </summary>
        public void Cancel()
        {
            if (IsComplete || !Cancellable || cancellation == null)
            {
                return;
            }

            IsCancelled = true;

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already torn down. The run is ending either way.
            }

            Complete();
        }

        /// <summary>
        /// Ends the run. The screen comes down once nothing is holding it.
        /// </summary>
        public void Complete()
        {
            if (IsComplete)
            {
                return;
            }

            IsComplete = true;

            for (int i = 0; i < steps.Length; i++)
            {
                steps[i].Complete();
            }

            Touch();
        }

        /// <summary>Completing it, so a <c>using</c> block brings the screen down even on a throw.</summary>
        public void Dispose()
        {
            Complete();

            if (cancellation != null)
            {
                cancellation.Dispose();
            }
        }

        internal void Touch()
        {
            var handler = Changed;

            if (handler == null)
            {
                return;
            }

            // A throwing subscriber must not strand the load that was reporting to it.
            try
            {
                handler();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        void Release()
        {
            if (holds > 0)
            {
                holds--;
                Touch();
            }
        }

        /// <summary>One hold. Releasing twice is ignored rather than driving the count negative.</summary>
        sealed class Holder : IDisposable
        {
            LoadingRun run;

            internal Holder(LoadingRun run)
            {
                this.run = run;
            }

            public void Dispose()
            {
                if (run == null)
                {
                    return;
                }

                run.Release();
                run = null;
            }
        }
    }
}
