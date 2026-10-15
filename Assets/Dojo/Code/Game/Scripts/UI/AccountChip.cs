using Dojo.Framework.Auth;
using TMPro;
using UnityEngine;
using VContainer;

namespace Dojo.Game.UI
{
    /// <summary>
    /// The home screen's account chip, top right: the signed-in email beside a round badge with
    /// its first letter.
    /// </summary>
    /// <remarks>
    /// The email is the one WorkOS handed back at sign-in. Without one - the Lobby opened on its
    /// own, or signed in before emails were kept - the chip says so rather than showing nothing.
    /// Injected by <c>LobbyLifetimeScope</c>; built by <c>Tools ▸ Dojo ▸ Build Lobby Layout</c>.
    /// </remarks>
    public sealed class AccountChip : MonoBehaviour
    {
        [SerializeField] TMP_Text email;
        [SerializeField] TMP_Text initial;

        IWorkOSAuthService auth;

        [Inject]
        public void Construct(IWorkOSAuthService workOS)
        {
            auth = workOS;
            Apply();
        }

        void OnEnable() => Apply();

        void Apply()
        {
            var address = auth != null ? auth.PlayerEmail : null;
            var known = !string.IsNullOrEmpty(address);

            if (email != null)
            {
                email.text = known ? address : "Signed in";
            }

            if (initial != null)
            {
                initial.text = known ? address.Substring(0, 1).ToUpperInvariant() : "?";
            }
        }
    }
}
