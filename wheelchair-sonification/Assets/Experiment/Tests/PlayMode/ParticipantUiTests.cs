using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WheelchairSonification.XR;
using WheelchairSonification.Runtime;
using WheelchairSonification.Core;
namespace WheelchairSonification.Tests
{
    public sealed class ParticipantUiTests
    {
        [UnityTest] public IEnumerator SerializedPrefabHasTwoIndependentBarsAndExclusiveModalities()
        {
            var camera=new GameObject("test camera").AddComponent<Camera>();var gauge=XrGauge.InstantiateFor(camera);
            var root=new GameObject("output");var output=root.AddComponent<UnityFeedbackOutput>();output.BindGauge(gauge);
            var f=new SpeedFeedbackEngine(new FeedbackSettings()).Update(0,.3,.9,true,StudyGroup.Visual,true);output.Apply(f);
            Assert.That(gauge.Visible,Is.True);Assert.That(gauge.leftFill.rect.width,Is.EqualTo(60.8).Within(.01));Assert.That(gauge.rightFill.rect.width,Is.EqualTo(182.4).Within(.01));
            f.group=StudyGroup.Auditory;output.Apply(f);Assert.That(gauge.Visible,Is.False);f.group=StudyGroup.Visual;f.enabled=false;output.Apply(f);Assert.That(gauge.Visible,Is.False);
            Assert.That(gauge.transform.GetComponentsInChildren<UnityEngine.UI.Button>().Length,Is.Zero);
            UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(camera.gameObject);yield return null;
        }
        [UnityTest] public IEnumerator PreparationPanelIsWorldAnchoredAndCanBeRecentered()
        {
            var root=new GameObject("XR preparation fixture");var xr=root.AddComponent<FocusVisionDemo>();xr.EnableEditorPreview();yield return null;
            xr.PreparationUi.ShowPreview(PreparationWorkflow.Preview("Home"));Canvas.ForceUpdateCanvases();
            Assert.That(xr.PreparationUi.Panel.IsChildOf(xr.Rig.Camera.transform),Is.False);
            var position=xr.PreparationUi.Panel.position;xr.Rig.Camera.transform.rotation=Quaternion.Euler(0,60,0);
            Assert.That(xr.PreparationUi.Panel.position,Is.EqualTo(position));xr.PreparationUi.Recenter();
            Assert.That(Vector3.Dot((xr.PreparationUi.Panel.position-xr.Rig.Camera.transform.position).normalized,xr.Rig.Camera.transform.forward),Is.GreaterThan(.98));
            xr.Rig.Camera.transform.rotation=Quaternion.Euler(35,60,0);xr.PreparationUi.Recenter();
            Assert.That(Vector3.Dot((xr.PreparationUi.Panel.position-xr.Rig.Camera.transform.position).normalized,xr.Rig.Camera.transform.forward),Is.GreaterThan(.98),"Looking down must still place the panel in front of the user's current view.");
            foreach(var label in xr.PreparationUi.Panel.GetComponentsInChildren<TMPro.TMP_Text>()){label.ForceMeshUpdate();Assert.That(label.isTextOverflowing,Is.False,label.text);}
            xr.PreparationUi.ShowPreview(PreparationWorkflow.Preview("Forward"));Assert.That(xr.PreparationUi.PanelVisible,Is.False);Assert.That(xr.PreparationUi.Footer.gameObject.activeSelf,Is.True);
            UnityEngine.Object.Destroy(root);yield return null;
        }
    }
}
