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
    }
}
