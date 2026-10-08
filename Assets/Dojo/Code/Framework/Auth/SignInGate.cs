using System;
using System.Threading;
using UnityEngine;

namespace Dojo.Framework.Auth
{
    /// <summary>Where sign-in stands, as the Lobby's cards need to know it.</summary>
    public enum SignInStage
    {
        /// <summary>Not known yet: startup is still checking, or is offline. No screen shows.</summary>
        Unknown,

        /// <summary>No saved session: the cards are up, waiting for the player to pick a way in.</summary>
        Waiting,

        /// <summary>A way in was picked and the browser is open.</summary>
        InProgress,

        /// <summary>A token is in hand, or there is nothing to sign in to: the main menu shows.</summary>
        SignedIn,
    }

    /// <summary>The way in the player picked, and the email they typed, if any.</summary>
    public readonly struct SignInChoice
    {
        public readonly SignInRoute Route;
        public readonly string Email;

        public SignInChoice(SignInRoute route, string email)
        {
            Route = route;
            Email = email ?? string.Empty;
        }
    }

    /// <summary>
    /// The hand-off between startup and the Lobby's Sign in and Create account cards.
    /// </summary>
    /// <remarks>
    /// Startup owns the sign-in and the Lobby owns the buttons, and neither should reach into the
    /// other. So startup waits here - <see cref="WaitForChoiceAsync"/> - and the cards, which watch
    /// <see cref="Stage"/>, show themselves and answer through <see cref="Choose"/>. Nothing here
    /// opens a browser; it only carries the decision across.
    /// <para>
    /// One choice per wait: a second click while the first is being acted on is refused, so a
    /// double-click cannot open two browser tabs.
    /// </para>
    /// </remarks>
    public sealed class SignInGate
    {
        AwaitableCompletionSource<SignInChoice> pending;
        CancellationTokenRegistration shutdown;

        public SignInStage Stage { get; private set; } = SignInStage.Unknown;

        /// <summary>Why the last attempt stopped, for the card to show. Empty when there is nothing to say.</summary>
        public string Message { get; private set; } = string.Empty;

        /// <summary>The stage or the message changed. Handed the gate, so a listener reads both.</summary>
        public event Action<SignInGate> Changed;

        /// <summary>Puts the cards up, with <paramref name="message"/>, and waits for a way in.</summary>
        public Awaitable<SignInChoice> WaitForChoiceAsync(string message, CancellationToken cancellationToken)
        {
            Release();

            pending = new AwaitableCompletionSource<SignInChoice>();
            var waiting = pending;
            shutdown = cancellationToken.Register(() =>
            {
                if (pending == waiting)
                {
                    pending = null;
                }

                waiting.TrySetCanceled();
            });

            Move(SignInStage.Waiting, message);
            return waiting.Awaitable;
        }

        /// <summary>
        /// The player picked a way in. True if startup was waiting for one; false otherwise - nobody
        /// asked yet, or this is the second click of a double-click.
        /// </summary>
        public bool Choose(SignInChoice choice)
        {
            if (pending == null || Stage != SignInStage.Waiting)
            {
                return false;
            }

            var waiting = pending;
            Release();

            Move(SignInStage.InProgress, string.Empty);
            waiting.TrySetResult(choice);
            return true;
        }

        /// <summary>A token is in hand, or there is nothing to sign in to. The cards go and the menu comes up.</summary>
        public void MarkSignedIn()
        {
            Release();
            Move(SignInStage.SignedIn, string.Empty);
        }

        void Release()
        {
            pending = null;
            shutdown.Dispose();
            shutdown = default;
        }

        void Move(SignInStage stage, string message)
        {
            Stage = stage;
            Message = message ?? string.Empty;
            Changed?.Invoke(this);
        }
    }
}
