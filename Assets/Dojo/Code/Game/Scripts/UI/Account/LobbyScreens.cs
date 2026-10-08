using Dojo.Framework.Auth;
using UnityEngine;
using VContainer;

namespace Dojo.Game.UI.Account
{
    /// <summary>
    /// Decides which of the Lobby's three screens is up - the main menu, the Sign in card or the
    /// Create account card - from where sign-in stands, and turns the cards' buttons into ways in
    /// for <see cref="SignInGate"/>.
    /// </summary>
    /// <remarks>
    /// The main menu is where both cards end: once the player is signed in, by either card or by a
    /// saved session, the cards go and the menu comes up. Until startup knows - still checking, or
    /// offline - none of the three shows.
    /// <para>
    /// Sits on the screens' parent, which stays active while all three are hidden. Opened on its
    /// own, without startup - the Lobby scene played directly - there is no gate, and it goes
    /// straight to the menu, as the menu itself already assumes.
    /// </para>
    /// <para>
    /// Built by <c>Tools ▸ Dojo ▸ Build Account Screens</c>, which fills in every reference below.
    /// </para>
    /// </remarks>
    public sealed class LobbyScreens : MonoBehaviour
    {
        [SerializeField] GameObject mainMenuScreen;
        [SerializeField] GameObject loginScreen;
        [SerializeField] GameObject registrationScreen;
        [SerializeField] SignInPanel signIn;
        [SerializeField] CreateAccountPanel createAccount;
        [SerializeField] CardMessage signInMessage;
        [SerializeField] CardMessage createAccountMessage;

        SignInGate gate;

        /// <summary>Which screens should be up at <paramref name="stage"/>, given which cards are up now.</summary>
        public static (bool menu, bool login, bool registration) Visible(SignInStage stage, bool loginShowing, bool registrationShowing)
        {
            switch (stage)
            {
                case SignInStage.SignedIn:
                    return (true, false, false);

                case SignInStage.Waiting:
                    // The card the player is on stays; with neither up, Sign in comes first.
                    return registrationShowing && !loginShowing ? (false, false, true) : (false, true, false);

                case SignInStage.InProgress:
                    // Under the loading screen while the browser is open. Left as they are, so a
                    // failed attempt comes back to the card the player used.
                    return (false, loginShowing, registrationShowing);

                default:
                    return (false, false, false);
            }
        }

        [Inject]
        public void Construct(SignInGate signInGate)
        {
            gate = signInGate;
            gate.Changed += OnChanged;
            Apply();
        }

        void Awake()
        {
            if (signIn != null)
            {
                signIn.SignInRequested += (email, password, keep) => Choose(SignInRoute.Email, email);
                signIn.ForgotPasswordRequested += () => Choose(SignInRoute.Email, signIn.Email);   // WorkOS's page has the reset link
                signIn.GoogleRequested += () => Choose(SignInRoute.Google, null);
                signIn.AppleRequested += () => Choose(SignInRoute.Apple, null);
            }

            if (createAccount != null)
            {
                createAccount.CreateAccountRequested += (name, email, password) => Choose(SignInRoute.SignUp, email);
                createAccount.GoogleRequested += () => Choose(SignInRoute.Google, null);
                createAccount.AppleRequested += () => Choose(SignInRoute.Apple, null);
            }

            Apply();
        }

        void OnDestroy()
        {
            if (gate != null)
            {
                gate.Changed -= OnChanged;
            }
        }

        void OnChanged(SignInGate changed) => Apply();

        void Choose(SignInRoute route, string email)
        {
            if (gate == null)
            {
                Debug.LogWarning("[Account] Nothing is waiting for a sign-in: the Lobby was opened without startup.", this);
                return;
            }

            gate.Choose(new SignInChoice(route, email));
        }

        void Apply()
        {
            var stage = gate != null ? gate.Stage : SignInStage.SignedIn;
            var visible = Visible(stage, Showing(loginScreen), Showing(registrationScreen));

            Set(mainMenuScreen, visible.menu);
            Set(loginScreen, visible.login);
            Set(registrationScreen, visible.registration);

            var message = gate != null && stage == SignInStage.Waiting ? gate.Message : string.Empty;

            if (signInMessage != null)
            {
                signInMessage.Show(message);
            }

            if (createAccountMessage != null)
            {
                createAccountMessage.Show(message);
            }
        }

        static bool Showing(GameObject screen) => screen != null && screen.activeSelf;

        static void Set(GameObject screen, bool on)
        {
            if (screen != null && screen.activeSelf != on)
            {
                screen.SetActive(on);
            }
        }
    }
}
