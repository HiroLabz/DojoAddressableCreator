using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI.Account
{
    /// <summary>
    /// The Create account card: name, email and password, a strength bar, the terms, and the ways
    /// to sign up.
    /// </summary>
    /// <remarks>
    /// Like <see cref="SignInPanel"/>, it behaves but creates nothing yet: CREATE ACCOUNT, Google and
    /// Apple are raised as events for the sign-in service to answer once it is wired. CREATE ACCOUNT
    /// only becomes pressable once <see cref="CanSubmit"/> is satisfied - the terms ticked, and an
    /// email that looks like one or none at all.
    /// <para>
    /// Built by <c>Tools ▸ Dojo ▸ Build Account Screens</c>, which fills in every reference below.
    /// </para>
    /// </remarks>
    public sealed class CreateAccountPanel : MonoBehaviour
    {
        [Header("Fields")]
        [SerializeField] TMP_InputField displayName;
        [SerializeField] TMP_InputField email;
        [SerializeField] TMP_InputField password;
        [SerializeField] Button showPassword;
        [SerializeField] TMP_Text showPasswordLabel;

        [Header("Strength")]
        [SerializeField] Image[] segments = new Image[PasswordStrength.Segments];
        [SerializeField] TMP_Text strengthLabel;
        [SerializeField] Image strengthMarker;
        [SerializeField] Color empty = new Color(0.11f, 0.16f, 0.25f, 1f);
        [SerializeField] Color weak = new Color(1f, 0.373f, 0.427f, 1f);
        [SerializeField] Color fair = new Color(1f, 0.71f, 0.29f, 1f);
        [SerializeField] Color strong = new Color(0.275f, 0.827f, 0.604f, 1f);

        [Header("Terms and actions")]
        [SerializeField] Toggle agree;
        [SerializeField] Button createAccount;
        [SerializeField] Button google;
        [SerializeField] Button apple;

        [Header("Switching")]
        [Tooltip("\"Sign in\": hides this screen and shows the one below.")]
        [SerializeField] Button signIn;
        [SerializeField] GameObject thisScreen;
        [SerializeField] GameObject loginScreen;

        /// <summary>CREATE ACCOUNT pressed: the name, the email and the password.</summary>
        public event Action<string, string, string> CreateAccountRequested;

        public event Action GoogleRequested;
        public event Action AppleRequested;

        /// <summary>
        /// Whether CREATE ACCOUNT can be pressed: the terms ticked, and the email either left empty
        /// or looking like one. The email is only filled in on WorkOS's page, where the player sets
        /// the password; a garbled one there would only be in the way.
        /// </summary>
        public static bool CanSubmit(string address, bool agreed)
        {
            return agreed && (string.IsNullOrWhiteSpace(address) || LooksLikeEmail(address));
        }

        /// <summary>Something before an @, and a dot somewhere after it. The server decides the rest.</summary>
        static bool LooksLikeEmail(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            var at = address.IndexOf('@');
            return at > 0 && address.IndexOf('.', at) > at + 1 && !address.EndsWith(".");
        }

        void Awake()
        {
            if (showPassword != null) showPassword.onClick.AddListener(TogglePassword);
            if (createAccount != null) createAccount.onClick.AddListener(RequestAccount);
            if (google != null) google.onClick.AddListener(() => GoogleRequested?.Invoke());
            if (apple != null) apple.onClick.AddListener(() => AppleRequested?.Invoke());
            if (signIn != null) signIn.onClick.AddListener(ShowLogin);
            if (agree != null) agree.onValueChanged.AddListener(_ => Refresh());

            foreach (var field in new[] { displayName, email, password })
            {
                if (field != null)
                {
                    field.onValueChanged.AddListener(_ => Refresh());
                }
            }

            PasswordReveal.Set(password, showPasswordLabel, false);
            Refresh();
        }

        void Refresh()
        {
            ShowStrength(PasswordStrength.Rate(Text(password)));

            if (createAccount != null)
            {
                createAccount.interactable = CanSubmit(Text(email), agree != null && agree.isOn);
            }
        }

        void ShowStrength(PasswordStrength.Rating rating)
        {
            PasswordStrengthView.Show(rating, segments, strengthLabel, strengthMarker, empty, weak, fair, strong);
        }

        void TogglePassword()
        {
            PasswordReveal.Set(password, showPasswordLabel, !PasswordReveal.IsShown(password));
        }

        void RequestAccount()
        {
            if (!createAccount.interactable)
            {
                return;
            }

            CreateAccountRequested?.Invoke(Text(displayName).Trim(), Text(email).Trim(), Text(password));
        }

        void ShowLogin()
        {
            if (loginScreen == null)
            {
                return;
            }

            loginScreen.SetActive(true);

            if (thisScreen != null)
            {
                thisScreen.SetActive(false);
            }
        }

        static string Text(TMP_InputField field) => field != null ? field.text : string.Empty;
    }
}
