using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dojo.Game.UI.Account
{
    /// <summary>
    /// The Sign in card: email, password and its strength, "keep me signed in", and the ways to
    /// sign in.
    /// </summary>
    /// <remarks>
    /// Looks and behaves, but signs nobody in yet. Everything that needs a server is raised as an
    /// event - <see cref="SignInRequested"/>, <see cref="GoogleRequested"/>,
    /// <see cref="AppleRequested"/>, <see cref="ForgotPasswordRequested"/> - for the sign-in service
    /// to answer once it is wired. SIGN IN is always pressable: it opens WorkOS's page, where the
    /// password is typed. "Create an account" swaps this card for the Create account one.
    /// <para>
    /// Built by <c>Tools ▸ Dojo ▸ Build Account Screens</c>, which fills in every reference below.
    /// </para>
    /// </remarks>
    public sealed class SignInPanel : MonoBehaviour
    {
        [Header("Fields")]
        [SerializeField] TMP_InputField email;
        [SerializeField] TMP_InputField password;
        [SerializeField] Button showPassword;
        [SerializeField] TMP_Text showPasswordLabel;
        [SerializeField] Toggle keepSignedIn;

        [Header("Strength")]
        [Tooltip("The same four bars and line as the Create account card, under the password.")]
        [SerializeField] Image[] segments = new Image[PasswordStrength.Segments];
        [SerializeField] TMP_Text strengthLabel;
        [SerializeField] Image strengthMarker;
        [SerializeField] Color empty = new Color(0.11f, 0.16f, 0.25f, 1f);
        [SerializeField] Color weak = new Color(1f, 0.373f, 0.427f, 1f);
        [SerializeField] Color fair = new Color(1f, 0.71f, 0.29f, 1f);
        [SerializeField] Color strong = new Color(0.275f, 0.827f, 0.604f, 1f);

        [Header("Actions")]
        [SerializeField] Button signIn;
        [SerializeField] Button forgotPassword;
        [SerializeField] Button google;
        [SerializeField] Button apple;

        [Header("Switching")]
        [Tooltip("\"Create an account\": hides this screen and shows the one below.")]
        [SerializeField] Button createAccount;
        [SerializeField] GameObject thisScreen;
        [SerializeField] GameObject registrationScreen;

        /// <summary>SIGN IN pressed: the email, the password, and whether to stay signed in.</summary>
        public event Action<string, string, bool> SignInRequested;

        public event Action GoogleRequested;
        public event Action AppleRequested;
        public event Action ForgotPasswordRequested;

        public string Email => email != null ? email.text.Trim() : string.Empty;

        public bool KeepSignedIn => keepSignedIn != null && keepSignedIn.isOn;

        void Awake()
        {
            Listen(showPassword, TogglePassword);
            Listen(signIn, RequestSignIn);
            Listen(forgotPassword, () => ForgotPasswordRequested?.Invoke());
            Listen(google, () => GoogleRequested?.Invoke());
            Listen(apple, () => AppleRequested?.Invoke());
            Listen(createAccount, ShowRegistration);

            if (password != null)
            {
                password.onValueChanged.AddListener(ShowStrength);
            }

            PasswordReveal.Set(password, showPasswordLabel, false);
            ShowStrength(password != null ? password.text : string.Empty);
        }

        void ShowStrength(string typed)
        {
            PasswordStrengthView.Show(PasswordStrength.Rate(typed), segments, strengthLabel, strengthMarker,
                empty, weak, fair, strong);
        }

        void TogglePassword()
        {
            PasswordReveal.Set(password, showPasswordLabel, !PasswordReveal.IsShown(password));
        }

        void RequestSignIn()
        {
            if (!signIn.interactable)
            {
                return;
            }

            SignInRequested?.Invoke(Email, password.text, KeepSignedIn);
        }

        void ShowRegistration()
        {
            if (registrationScreen == null)
            {
                return;
            }

            registrationScreen.SetActive(true);

            if (thisScreen != null)
            {
                thisScreen.SetActive(false);
            }
        }

        static void Listen(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }
    }

    /// <summary>The password strength bars and line, shared by both cards.</summary>
    public static class PasswordStrengthView
    {
        /// <summary>
        /// Fills as many bars as the rating scores, in its colour - weak, fair or strong - and
        /// shows the line saying so. Nothing typed: every bar empty, no line.
        /// </summary>
        public static void Show(PasswordStrength.Rating rating, Image[] segments, TMP_Text label, Image marker,
            Color empty, Color weak, Color fair, Color strong)
        {
            var colour = rating.Score <= 1 ? weak : rating.Score == 2 ? fair : strong;

            if (segments != null)
            {
                for (var i = 0; i < segments.Length; i++)
                {
                    if (segments[i] != null)
                    {
                        segments[i].color = i < rating.Score ? colour : empty;
                    }
                }
            }

            var any = rating.Score > 0;

            if (label != null)
            {
                label.text = rating.Description;
                label.color = colour;
                label.enabled = any;
            }

            if (marker != null)
            {
                marker.color = colour;
                marker.enabled = any;
            }
        }
    }

    /// <summary>The Show / Hide switch on a password field, shared by both cards.</summary>
    public static class PasswordReveal
    {
        public static bool IsShown(TMP_InputField field)
            => field != null && field.contentType != TMP_InputField.ContentType.Password;

        public static void Set(TMP_InputField field, TMP_Text label, bool shown)
        {
            if (field != null)
            {
                field.contentType = shown ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
                field.ForceLabelUpdate();
            }

            if (label != null)
            {
                label.text = shown ? "Hide" : "Show";
            }
        }
    }
}
