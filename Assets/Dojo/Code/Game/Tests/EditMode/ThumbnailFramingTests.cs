using System.Collections.Generic;
using Dojo.Game.Placement;
using NUnit.Framework;
using UnityEngine;

namespace Dojo.Game.Tests.EditMode
{
    /// <summary>
    /// Thumbnails show the whole piece (Jacob, 27 Nov: the item, room and floor thumbnails were cut
    /// off), and the Departments tab's picture covers the department's areas.
    /// </summary>
    public sealed class ThumbnailFramingTests
    {
        Camera camera;

        [SetUp]
        public void SetUp()
        {
            camera = new GameObject("ThumbnailFramingTests").AddComponent<Camera>();
            camera.enabled = false;
            camera.aspect = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(camera.gameObject);
        }

        /// <summary>Every corner of the box, as the camera sees it, in 0..1.</summary>
        List<Vector3> Seen(Bounds bounds)
        {
            var seen = new List<Vector3>();
            var e = bounds.extents;

            for (var i = 0; i < 8; i++)
            {
                var corner = bounds.center + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);

                seen.Add(camera.WorldToViewportPoint(corner));
            }

            return seen;
        }

        // The pieces that were cut: a cube-ish side table, a wide meeting table, a tall rack and a room.
        [TestCase(0.6f, 0.6f, 0.6f, 20f)]
        [TestCase(2.4f, 0.8f, 1.6f, 20f)]
        [TestCase(0.6f, 2.2f, 0.9f, 20f)]
        [TestCase(6f, 3f, 6f, 40f)]
        public void TheWholeBox_IsInThePicture_WithAMargin(float x, float y, float z, float pitch)
        {
            var bounds = new Bounds(new Vector3(3f, y * 0.5f, -2f), new Vector3(x, y, z));

            ThumbnailFraming.Apply(camera, ThumbnailFraming.Fit(bounds, pitch, 135f, 1.1f));

            foreach (var p in Seen(bounds))
            {
                Assert.That(p.x, Is.InRange(0.03f, 0.97f), "across");
                Assert.That(p.y, Is.InRange(0.03f, 0.97f), "up and down");
                Assert.That(p.z, Is.GreaterThan(camera.nearClipPlane), "in front of the camera");
                Assert.That(p.z, Is.LessThan(camera.farClipPlane), "not past the far plane");
            }
        }

        [Test]
        public void ThePicture_IsFilled_NotLeftMostlyEmpty()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(0.6f, 0.6f, 0.6f));

            ThumbnailFraming.Apply(camera, ThumbnailFraming.Fit(bounds, 20f, 135f, 1.1f));

            float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
            foreach (var p in Seen(bounds))
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }

            Assert.That(Mathf.Max(maxX - minX, maxY - minY), Is.GreaterThan(0.85f), "a tight fit, only the padding left over");
            Assert.AreEqual(0.5f, (minX + maxX) * 0.5f, 0.01f, "centred across");
            Assert.AreEqual(0.5f, (minY + maxY) * 0.5f, 0.01f, "centred up and down");
        }

        [Test]
        public void TheOldFit_CutTheSideTable()
        {
            // What the thumbnails did before: the view sized to the largest half-width.
            var bounds = new Bounds(Vector3.zero, new Vector3(0.6f, 0.6f, 0.6f));
            var direction = Quaternion.Euler(20f, 135f, 0f) * Vector3.forward;
            var extent = 0.3f;
            camera.orthographic = true;
            camera.orthographicSize = extent * 1.15f;
            camera.transform.SetPositionAndRotation(bounds.center - direction * (extent * 4f), Quaternion.LookRotation(direction));
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = extent * 20f;

            var cut = false;
            foreach (var p in Seen(bounds))
            {
                cut |= p.x < 0f || p.x > 1f || p.y < 0f || p.y > 1f;
            }

            Assert.IsTrue(cut, "the reason for the fix");
        }

        [Test]
        public void ADepartmentsPicture_CoversItsAreas_OnItsLowestFloor_UpToAWall()
        {
            var ground = new BlockArea(1, "Dev Lab", 0, 0.3f, new List<Vector3> { new Vector3(0.15f, 0.05f, 0.15f), new Vector3(2.85f, 0.05f, 1.35f) });
            var upstairs = new BlockArea(2, "Dev Lab 2", 1, 0.3f, new List<Vector3> { new Vector3(20f, 4.05f, 20f) });

            Bounds bounds;
            Assert.IsTrue(AreaPictures.TryBounds(new[] { upstairs, ground }, out bounds));

            Assert.AreEqual(0f, bounds.min.x, 0.001f);
            Assert.AreEqual(3f, bounds.max.x, 0.001f);
            Assert.AreEqual(0f, bounds.min.z, 0.001f);
            Assert.AreEqual(1.5f, bounds.max.z, 0.001f);
            Assert.That(bounds.max.y, Is.GreaterThanOrEqualTo(0.05f + AreaPictures.Height - 0.001f), "up to the top of a wall");
            Assert.That(bounds.max.x, Is.LessThan(19f), "the upstairs area is not in the picture");
        }

        [Test]
        public void NoCells_NoPicture()
        {
            Bounds bounds;
            Assert.IsFalse(AreaPictures.TryBounds(new BlockArea[0], out bounds));
            Assert.IsFalse(AreaPictures.TryBounds(null, out bounds));
            Assert.IsNull(AreaPictures.Picture(new BlockArea[0]));
        }
    }
}
