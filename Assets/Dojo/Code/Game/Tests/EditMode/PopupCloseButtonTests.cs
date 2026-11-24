using System.Collections.Generic;
using System.IO;
using Dojo.Framework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// Every popup prefab can be closed without choosing (ClickUp 14ze67xg5my): the X is wired,
    /// it is not one of the row's buttons, and a popup that must be answered can hide it.
    /// </summary>
    public sealed class PopupCloseButtonTests
    {
        const string Folder = "Assets/Dojo/Prefabs/UI/Popups";

        readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in made)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            made.Clear();
        }

        static IEnumerable<string> PopupPrefabs()
        {
            foreach (var path in Directory.GetFiles(Folder, "*.prefab"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<MessagePopup>(path.Replace('\\', '/'));
                if (asset != null)
                {
                    yield return path.Replace('\\', '/');
                }
            }
        }

        [Test]
        public void ThereArePopupPrefabsToCheck()
        {
            Assert.That(new List<string>(PopupPrefabs()).Count, Is.GreaterThanOrEqualTo(6), "Notice, Affirmation, Confirmation, ThreeWay, Prompt, Department (the creator's; the game has nine)");
        }

        [Test]
        public void EveryPopupPrefab_HasTheCloseButton_OutsideItsRowOfButtons()
        {
            foreach (var path in PopupPrefabs())
            {
                var popup = AssetDatabase.LoadAssetAtPath<MessagePopup>(path);

                Assert.IsTrue(popup.Has(MessagePopup.Choice.Dismissed), path + ": the X is wired (Tools > Dojo > Build Popup Close Buttons)");
                Assert.IsTrue(popup.IsDismissible, path + ": the X starts shown");
                CollectionAssert.DoesNotContain(popup.Choices, MessagePopup.Choice.Dismissed, path + ": the X is not in the row");
            }
        }

        [Test]
        public void APopupThatMustBeAnswered_HidesTheX()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<MessagePopup>(Folder + "/NoticePopup.prefab");
            var popup = Object.Instantiate(prefab);
            made.Add(popup.gameObject);

            Assert.IsTrue(popup.IsDismissible);
            popup.Dismissible(false);
            Assert.IsFalse(popup.IsDismissible, "the sign-in lock counts down and goes by itself");
            popup.Dismissible(true);
            Assert.IsTrue(popup.IsDismissible);
        }
    }
}
