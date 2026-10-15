using Dojo.Framework.UI;
using UnityEngine;
// using UnityEngine.InputSystem;

namespace Dojo.Game.Dev
{
    /// <summary>
    /// TEMPORARY - a way to look at the four popups. 1 to 4 shows each one; 5 closes them all,
    /// which is the only way out of the Notice, since it has no buttons. Delete once the popups
    /// are called from the game itself.
    /// </summary>
    /// <remarks>
    /// Sits on the Bootstrapper's <c>PopupTest</c> object and survives every scene load after it,
    /// so the keys work in the Lobby and the game as well. A plain MonoBehaviour, no container: it
    /// needs nothing but the prefabs. Every press of a popup's button is logged, so the choice it
    /// reports shows in the console. Not Escape for closing: placement and the loading screen
    /// already use it.
    /// <para>
    /// Switched off: its keys fired while a name was being typed, so a "2" put the Affirmation
    /// popup - whose stand-in text reads "World saved" - over the save dialog. The methods are
    /// commented out rather than deleted; <see cref="popups"/> stays so the Bootstrapper keeps its
    /// four prefab references for when they come back.
    /// </para>
    /// </remarks>
    public sealed class PopupTest : MonoBehaviour
    {
        // static readonly Key[] ShowKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };
        // static readonly Key[] ShowPadKeys = { Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4 };

        // static PopupTest alive;

        [Tooltip("Shown by 1, 2, 3 and 4, in this order.")]
        [SerializeField] MessagePopup[] popups = new MessagePopup[4];

        // /// <summary>Cleared at the start of every play session, for when domain reload is off.</summary>
        // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        // static void ForgetLastSession()
        // {
        //     alive = null;
        // }

        // void Awake()
        // {
        //     // One is enough, should the Bootstrapper ever load a second time.
        //     if (alive != null && alive != this)
        //     {
        //         Destroy(gameObject);
        //         return;
        //     }
        //
        //     alive = this;
        //     DontDestroyOnLoad(gameObject);
        // }

        // void OnDestroy()
        // {
        //     if (alive == this)
        //     {
        //         alive = null;
        //     }
        // }

        // void Update()
        // {
        //     var keyboard = Keyboard.current;
        //     if (keyboard == null)
        //     {
        //         return;
        //     }
        //
        //     if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame)
        //     {
        //         PopupManager.Instance.HideAll();
        //         return;
        //     }
        //
        //     for (var i = 0; i < popups.Length && i < ShowKeys.Length; i++)
        //     {
        //         if (keyboard[ShowKeys[i]].wasPressedThisFrame || keyboard[ShowPadKeys[i]].wasPressedThisFrame)
        //         {
        //             Show(i);
        //         }
        //     }
        // }

        // void Show(int slot)
        // {
        //     var prefab = popups[slot];
        //     if (prefab == null)
        //     {
        //         Debug.LogWarning("[PopupTest] Nothing in slot " + (slot + 1) + ".", this);
        //         return;
        //     }
        //
        //     var popup = PopupManager.Instance.Show(prefab);
        //     popup.Chosen += choice => Debug.Log("[PopupTest] " + prefab.name + " answered " + choice + ".");
        //
        //     Debug.Log("[PopupTest] Showing " + prefab.name + ".");
        // }
    }
}
