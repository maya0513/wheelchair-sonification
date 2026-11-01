using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VIVE.OpenXR;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;
using WheelchairSonification.XR;

namespace WheelchairSonification.Tests
{
    public sealed class ParticipantUiTests
    {
        [Test]
        public void OpenXREyePoseConvertsToUnityTrackingCoordinates()
        {
            var position = new XrVector3f(1, 2, -3).ToUnityVector();
            Assert.That(position, Is.EqualTo(new Vector3(1, 2, 3)));
            var nativeYaw = new XrQuaternionf(0, Mathf.Sqrt(.5f), 0, Mathf.Sqrt(.5f));
            Assert.That(
                Vector3.Distance(nativeYaw.ToUnityQuaternion() * Vector3.forward, Vector3.left),
                Is.LessThan(.00001f)
            );
        }

        [UnityTest]
        public IEnumerator SerializedPrefabHasTwoIndependentBarsAndExclusiveModalities()
        {
            var camera = new GameObject("test camera").AddComponent<Camera>();
            var gauge = XrGauge.InstantiateFor(camera);
            var source = new SyntheticPoseSource();
            var frame = source.Read(0);
            var calibration = SyntheticPoseSource.Calibration();
            gauge.Anchor.Align(frame, calibration, Vector3.zero, Quaternion.identity);
            gauge.Follow(
                frame,
                calibration,
                camera.transform.parent ?? new GameObject("origin").transform
            );
            var root = new GameObject("output");
            var output = root.AddComponent<UnityFeedbackOutput>();
            output.BindGauge(gauge);
            var f = new SpeedFeedbackEngine(new FeedbackSettings()).Update(
                0,
                .3,
                .9,
                true,
                StudyGroup.Visual,
                true
            );
            output.Apply(f);
            Assert.That(gauge.Visible, Is.True);
            Assert.That(gauge.leftFill.rect.height, Is.EqualTo(64).Within(.01));
            Assert.That(gauge.rightFill.rect.height, Is.EqualTo(192).Within(.01));
            f.group = StudyGroup.Auditory;
            output.Apply(f);
            Assert.That(gauge.Visible, Is.False);
            f.group = StudyGroup.Visual;
            f.enabled = false;
            output.Apply(f);
            Assert.That(gauge.Visible, Is.False);
            Assert.That(
                gauge.transform.GetComponentsInChildren<UnityEngine.UI.Button>().Length,
                Is.Zero
            );
            Assert.That(gauge.transform.IsChildOf(camera.transform), Is.False);
            var old = gauge.transform.position;
            camera.transform.rotation = Quaternion.Euler(0, 60, 0);
            Assert.That(gauge.transform.position, Is.EqualTo(old));
            Assert.That(gauge.leftFill.pivot, Is.EqualTo(Vector2.zero));
            Assert.That(
                gauge.leftFill.GetComponent<UnityEngine.UI.Image>().color.a,
                Is.EqualTo(.7f).Within(.001)
            );
            UnityEngine.Object.Destroy(gauge.gameObject);
            UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(camera.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PreparationPanelIsWorldAnchoredAndCanBeRecentered()
        {
            var root = new GameObject("XR preparation fixture");
            var xr = root.AddComponent<FocusVisionDemo>();
            xr.EnableEditorPreview();
            yield return null;
            xr.PreparationUi.ShowPreview(PreparationWorkflow.Preview("Home"));
            Canvas.ForceUpdateCanvases();
            Assert.That(xr.PreparationUi.Panel.IsChildOf(xr.Rig.Camera.transform), Is.False);
            var position = xr.PreparationUi.Panel.position;
            xr.Rig.Camera.transform.rotation = Quaternion.Euler(0, 60, 0);
            Assert.That(xr.PreparationUi.Panel.position, Is.EqualTo(position));
            xr.PreparationUi.Recenter();
            Assert.That(
                Vector3.Dot(
                    (xr.PreparationUi.Panel.position - xr.Rig.Camera.transform.position).normalized,
                    xr.Rig.Camera.transform.forward
                ),
                Is.GreaterThan(.98)
            );
            xr.Rig.Camera.transform.rotation = Quaternion.Euler(35, 60, 0);
            xr.PreparationUi.Recenter();
            Assert.That(
                Vector3.Dot(
                    (xr.PreparationUi.Panel.position - xr.Rig.Camera.transform.position).normalized,
                    xr.Rig.Camera.transform.forward
                ),
                Is.GreaterThan(.98),
                "Looking down must still place the panel in front of the user's current view."
            );
            foreach (var label in xr.PreparationUi.Panel.GetComponentsInChildren<TMPro.TMP_Text>())
            {
                label.ForceMeshUpdate();
                Assert.That(label.isTextOverflowing, Is.False, label.text);
            }
            xr.PreparationUi.ShowPreview(PreparationWorkflow.Preview("Forward"));
            Assert.That(xr.PreparationUi.PanelVisible, Is.False);
            Assert.That(xr.PreparationUi.Footer.gameObject.activeSelf, Is.True);
            UnityEngine.Object.Destroy(root);
            yield return null;
        }
    }
}
