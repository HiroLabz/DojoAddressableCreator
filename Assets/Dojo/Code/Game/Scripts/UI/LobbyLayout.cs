using System;
using TMPro;
using UnityEngine;

namespace Dojo.Game.UI
{
    /// <summary>
    /// Moves the Lobby's shared pieces - the mascot, the title, the brand label - between its two
    /// layouts: the home screen's, and the one the sign-in and registration cards share, where the
    /// same pieces sit on a brand panel down the left and the card has the right column.
    /// </summary>
    /// <remarks>
    /// Shared rather than copied into each screen, so the mascot's shuffle and the title are one
    /// thing wherever they sit. Which layout is decided by which screens are up, not by being told:
    /// <see cref="Account.LobbyScreens"/> already owns that, and a second opinion could disagree.
    /// With neither card up - home, or startup still checking - it is the home layout.
    /// <para>
    /// Built by <c>Tools ▸ Dojo ▸ Build Lobby Layout</c>, which fills in every placement below.
    /// Every placement is measured from the top-left or top-right corner its target is anchored to.
    /// </para>
    /// </remarks>
    public sealed class LobbyLayout : MonoBehaviour
    {
        /// <summary>Where one shared piece sits in each layout.</summary>
        [Serializable]
        public sealed class Placement
        {
            public RectTransform target;

            public Vector2 home;
            public Vector2 homeSize;

            [Tooltip("Font size at home, for a piece of text. Zero leaves it alone.")]
            public float homeFont;

            public Vector2 account;
            public Vector2 accountSize;

            [Tooltip("Font size on the sign-in and registration screens. Zero leaves it alone.")]
            public float accountFont;
        }

        [SerializeField] GameObject loginScreen;
        [SerializeField] GameObject registrationScreen;
        [SerializeField] Placement[] placements = new Placement[0];

        [Tooltip("Shown only with a card up: the brand panel, its reasons, its footer, the build chip.")]
        [SerializeField] GameObject[] accountOnly = new GameObject[0];

        // -1 until the first layout is applied, so the first frame always places everything.
        int applied = -1;

        /// <summary>True while the sign-in or registration card is up.</summary>
        public bool AccountLayout => Showing(loginScreen) || Showing(registrationScreen);

        void OnEnable() => applied = -1;

        void LateUpdate()
        {
            var mode = AccountLayout ? 1 : 0;

            if (mode != applied)
            {
                applied = mode;
                Apply(mode == 1);
            }
        }

        /// <summary>Puts every shared piece where it belongs in one layout or the other.</summary>
        public void Apply(bool account)
        {
            foreach (var placement in placements)
            {
                if (placement == null || placement.target == null)
                {
                    continue;
                }

                placement.target.anchoredPosition = account ? placement.account : placement.home;
                placement.target.sizeDelta = account ? placement.accountSize : placement.homeSize;

                var font = account ? placement.accountFont : placement.homeFont;
                var text = font > 0f ? placement.target.GetComponent<TMP_Text>() : null;

                if (text != null)
                {
                    text.fontSize = font;
                }
            }

            foreach (var piece in accountOnly)
            {
                if (piece != null && piece.activeSelf != account)
                {
                    piece.SetActive(account);
                }
            }
        }

        static bool Showing(GameObject screen) => screen != null && screen.activeInHierarchy;
    }
}
