using System.Collections;
using System.Linq;
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
            Assert.That(gauge.leftFill.rect.height, Is.EqualTo(128).Within(.01));
            Assert.That(gauge.rightFill.rect.height, Is.EqualTo(320).Within(.01));
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
                Is.EqualTo(.85f).Within(.001)
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
            Assert.That(
                xr.PreparationUi.Panel.GetComponentsInChildren<UnityEngine.UI.Button>()
                    .Count(b => b.name.StartsWith("Preparation entry")),
                Is.EqualTo(4)
            );
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
            xr.PreparationUi.ShowPreview(PreparationWorkflow.Preview("Home", true));
            var register = xr
                .PreparationUi.Panel.Find("Preparation entry 0")
                .GetComponent<UnityEngine.UI.Button>();
            Assert.That(register.interactable, Is.False);
            Assert.That(
                xr.PreparationUi.Panel.Find("Detail").GetComponent<TMPro.TMP_Text>().text,
                Does.Contain("2／3")
            );
            xr.PreparationUi.ShowPreview(PreparationWorkflow.Preview("Home"));
            Assert.That(register.interactable, Is.True);
            Assert.That(
                xr.PreparationUi.Panel.Find("Detail").GetComponent<TMPro.TMP_Text>().text,
                Does.Not.Contain("2／3")
            );
            xr.PreparationUi.ShowPreview(PreparationWorkflow.Preview("Forward"));
            Assert.That(xr.PreparationUi.PanelVisible, Is.False);
            Assert.That(xr.PreparationUi.Footer.gameObject.activeSelf, Is.True);
            UnityEngine.Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HeadsetMenuStopsFreeRunAndCanRecoverMissingTracking()
        {
            var root = new GameObject("XR free run fixture");
            var xr = root.AddComponent<FocusVisionDemo>();
            xr.EnableEditorPreview();
            yield return null;
            xr.enabled = xr.Research.enabled = xr.VisitHost.enabled = false;
            xr.Research.Tick(0);
            double time = 0;
            bool missing = false;
            void Tick()
            {
                time += .025;
                var frame = PreparationInference.Copy(xr.Research.Frame);
                frame.time = time;
                if (missing)
                    frame.Find("synthetic-2").valid = false;
                xr.VisitHost.Preparation.Tick(frame, time, true, xr.Research.Settings, .1f, true);
                xr.PreparationUi.Render(false, "123456", true);
            }
            for (int i = 0; i < 90; i++)
                Tick();
            var calibration = xr.Research.Wizard.Calibration;
            Assert.That(xr.VisitHost.ExecuteLocal("alignDisplay").ok, Is.True);
            Assert.That(xr.VisitHost.ExecuteLocal("alignDisplay").ok, Is.True);
            Assert.That(xr.Research.Wizard.Calibration, Is.SameAs(calibration));
            Assert.That(xr.Gauge.Anchor.IsValid(xr.Research.Frame, calibration), Is.True);
            Assert.That(xr.VisitHost.ExecuteLocal("prep.freeVisual").ok, Is.True);
            for (int i = 0; i < 130; i++)
                Tick();
            Assert.That(xr.VisitHost.Preparation.Stage, Is.EqualTo("Free"));
            Assert.That(xr.PreparationUi.PanelVisible, Is.False);
            xr.PreparationUi.Toggle(); // Same path as the controller's A/X action.
            Assert.That(xr.VisitHost.Preparation.Stage, Is.EqualTo("FreeStopping"));
            Assert.That(root.GetComponent<UnityFeedbackOutput>().Last.enabled, Is.False);
            for (int i = 0; i < 90; i++)
                Tick();
            Assert.That(xr.PreparationUi.PanelVisible, Is.True);
            Assert.That(xr.VisitHost.Preparation.Stage, Is.EqualTo("Result"));
            Assert.That(xr.VisitHost.ExecuteLocal("prep.freeVisual").ok, Is.True);
            for (int i = 0; i < 130; i++)
                Tick();
            missing = true;
            Tick();
            xr.PreparationUi.Toggle();
            Tick();
            Assert.That(xr.VisitHost.Preparation.Stage, Is.EqualTo("FreeStopping"));
            Assert.That(
                xr.PreparationUi.Footer.GetComponentInChildren<TMPro.TMP_Text>().text,
                Does.Contain("A／X")
            );
            xr.PreparationUi.Toggle();
            Tick();
            Assert.That(xr.PreparationUi.PanelVisible, Is.True);
            Assert.That(xr.VisitHost.Preparation.View.stopConfirmedManually, Is.True);
            Assert.That(xr.VisitHost.ExecuteLocal("prep.freeVisual").ok, Is.False);
            UnityEngine.Object.Destroy(root);
            yield return null;
        }
    }
}
