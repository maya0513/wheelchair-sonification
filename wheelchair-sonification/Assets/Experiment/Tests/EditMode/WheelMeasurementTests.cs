using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;
namespace WheelchairSonification.Tests
{
    public sealed class WheelMeasurementTests
    {
        [TestCase(.4,.6)][TestCase(-.4,.6)][TestCase(0,0)][TestCase(1.49,-1.2)]
        public void CamberedWheelsAndMovingBody(double left,double right)
        {using var source=new SyntheticPoseSource{LeftRps=left,RightRps=right};var e=new WheelSpeedEstimator(SyntheticPoseSource.Calibration());e.Update(source.Read(2));var s=e.Update(source.Read(2.01));Assert.That(s.valid,Is.True);Assert.That(s.leftRps,Is.EqualTo(left).Within(.0001));Assert.That(s.rightRps,Is.EqualTo(right).Within(.0001));}
        [Test] public void WrapAndGap(){using var source=new SyntheticPoseSource{LeftRps=1,RightRps=1};var e=new WheelSpeedEstimator(SyntheticPoseSource.Calibration());e.Update(source.Read(359.0/360));Assert.That(e.Update(source.Read(361.0/360)).leftRps,Is.EqualTo(1).Within(.0001));Assert.That(e.Update(source.Read(2)).valid,Is.False);Assert.That(e.Update(source.Read(2.01)).valid,Is.True);}
        [Test] public void WristMissingDoesNotMute(){using var source=new SyntheticPoseSource();var e=new WheelSpeedEstimator(SyntheticPoseSource.Calibration());e.Update(source.Read(0));var f=source.Read(.01);f.poses[0].valid=f.poses[1].valid=false;Assert.That(e.Update(f).valid,Is.True);}
        [Test] public void WheelMissingCannotBridge(){using var source=new SyntheticPoseSource();var e=new WheelSpeedEstimator(SyntheticPoseSource.Calibration());e.Update(source.Read(0));var f=source.Read(.01);f.poses[2].valid=false;Assert.That(e.Update(f).valid,Is.False);Assert.That(e.Update(source.Read(.02)).valid,Is.False);Assert.That(e.Update(source.Read(.03)).valid,Is.True);}
        [Test] public void ReconnectInvalidatesCalibration(){using var source=new SyntheticPoseSource();var c=SyntheticPoseSource.Calibration();var e=new WheelSpeedEstimator(c);e.Update(source.Read(0));var f=source.Read(.01);f.poses[3].runtimeDeviceId++;Assert.That(e.Update(f).valid,Is.False);Assert.That(c.Complete,Is.False);}
        [Test] public void DuplicateNotNew(){using var source=new SyntheticPoseSource();var e=new WheelSpeedEstimator(SyntheticPoseSource.Calibration());e.Update(source.Read(0));e.Update(source.Read(.01));var s=e.Update(source.Read(.01));Assert.That(s.fresh,Is.False);}
        [Test] public void DuplicateBeforeFirstDifferenceRemainsInvalid()
        {
            using var source = new SyntheticPoseSource();
            var estimator = new WheelSpeedEstimator(SyntheticPoseSource.Calibration());
            Assert.That(estimator.Update(source.Read(0)).valid, Is.False);
            var duplicate = source.Read(0); duplicate.time = .01;
            Assert.That(estimator.Update(duplicate).valid, Is.False);
            Assert.That(estimator.Update(source.Read(.02)).valid, Is.True);

            var missing = source.Read(.03); missing.poses.RemoveAt((int)TrackerRole.LeftWheel);
            Assert.That(estimator.Update(missing).valid, Is.False);
            Assert.That(estimator.Update(source.Read(.04)).valid, Is.False);
            duplicate = source.Read(.04); duplicate.time = .05;
            Assert.That(estimator.Update(duplicate).valid, Is.False);
            Assert.That(estimator.Update(source.Read(.06)).valid, Is.True);
        }
        [Test] public void StaleDuplicateCannotRestoreAPlaceholderSpeed()
        {
            using var source = new SyntheticPoseSource();
            var estimator = new WheelSpeedEstimator(SyntheticPoseSource.Calibration());
            estimator.Update(source.Read(0));
            Assert.That(estimator.Update(source.Read(.01)).valid, Is.True);
            foreach (double acquired in new[] { .12, .13, .14 })
            {
                var duplicate = source.Read(.01); duplicate.time = acquired;
                Assert.That(estimator.Update(duplicate).valid, Is.False);
            }
            Assert.That(estimator.Update(source.Read(.15)).valid, Is.False);
            Assert.That(estimator.Update(source.Read(.16)).valid, Is.True);
        }
        [Test] public void RoleDuplicateRejected(){using var source=new SyntheticPoseSource();var w=new CalibrationWizard();var f=source.Read(0);w.Bind(TrackerRole.Chassis,f.poses[4]);Assert.Throws<ArgumentException>(()=>w.Bind(TrackerRole.LeftWheel,f.poses[4]));}
        [Test] public void OptionalRoleCanBeClearedAndCriticalRemovalInvalidatesAxes()
        {var w=new CalibrationWizard();w.UseSynthetic(SyntheticPoseSource.Calibration());w.Unbind(TrackerRole.LeftWrist);Assert.That(w.Calibration.Complete,Is.True);Assert.That(w.Calibration.roles[0].key,Is.Null);w.Unbind(TrackerRole.LeftWheel);Assert.That(w.Calibration.Complete,Is.False);}
        [Test] public void RotationCalibrationWorksWithCamber()
        {using var source=new SyntheticPoseSource();var w=new CalibrationWizard();var f=source.Read(0);foreach(TrackerRole r in Enum.GetValues(typeof(TrackerRole)))w.Bind(r,f.poses[(int)r]);w.Capture(f);for(int i=1;i<=40;i++)w.Observe(source.Read(i*.01));w.Capture(source.Read(.4));for(int i=41;i<=71;i++)w.Observe(source.Read(i*.01));w.Capture(source.Read(.71));Assert.That(w.Calibration.Complete,Is.True);Assert.That(Vector3.Angle(w.Calibration.left.axle,SyntheticPoseSource.Calibration().left.axle),Is.LessThan(.02));}
        [Test] public void RecordedDataReplaysIdenticallyAndOldFormatRejected()
        {
            string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
            try{using var source=new SyntheticPoseSource();var c=SyntheticPoseSource.Calibration();var estimator=new WheelSpeedEstimator(c);var engine=new SpeedFeedbackEngine(new FeedbackSettings());var settings=new ResearchSessionSettings{trial_id="test-trial",input_source=source.Kind,phase="Training",group="Visual"};
            using(var log=new ResearchSessionLog(root,settings,c)){for(int i=0;i<50;i++){var f=source.Read(i*.01);log.Poses(f,c);var s=estimator.Update(f);var v=engine.Update(f.time,s.leftRps,s.rightRps,s.valid,StudyGroup.Visual,true);log.Speed(f.time,s,v);}log.Complete(.5,true,"test");}
            string path=Path.Combine(root,"test-trial");Assert.That(PoseReplay.Run(path,Path.Combine(root,"replay")).classifications_match,Is.True);
            File.WriteAllText(Path.Combine(path,"session.json"),"{\"format\":1}");Assert.Throws<InvalidDataException>(()=>PoseReplay.Run(path,Path.Combine(root,"old")));
            }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        [TestCase(false)] [TestCase(true)]
        public void MissingDeviceAndChangedDeviceHaveDistinctReplaySemantics(bool reconnect)
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                using var source = new SyntheticPoseSource();
                var calibration = SyntheticPoseSource.Calibration();
                var estimator = new WheelSpeedEstimator(calibration);
                var engine = new SpeedFeedbackEngine(new FeedbackSettings());
                var settings = new ResearchSessionSettings { trial_id = "loss-trial", input_source = source.Kind, phase = "Training", group = "Visual" };
                using (var log = new ResearchSessionLog(root, settings, calibration))
                {
                    for (int i = 0; i < 6; i++)
                    {
                        var frame = source.Read(i * .01);
                        if (reconnect && i >= 2) frame.poses[(int)TrackerRole.LeftWheel].runtimeDeviceId += 100;
                        else if (!reconnect && i == 2) frame.poses.RemoveAt((int)TrackerRole.LeftWheel);
                        log.Poses(frame, calibration);
                        var sample = estimator.Update(frame);
                        if (i == 4) Assert.That(sample.valid, Is.EqualTo(!reconnect));
                        var value = engine.Update(frame.time, sample.leftRps, sample.rightRps, sample.valid, StudyGroup.Visual, true);
                        log.Speed(frame.time, sample, value);
                    }
                    log.Complete(.06, true, "test");
                }
                Assert.That(calibration.Complete, Is.EqualTo(!reconnect));
                string trial = Path.Combine(root, "loss-trial");
                using (var recorded = new RecordedPoseSource(Path.Combine(trial, "poses.csv")))
                {
                    var pose = recorded.Frames[2].Find(calibration.roles[(int)TrackerRole.LeftWheel].key);
                    if (reconnect) Assert.That(pose.runtimeDeviceId, Is.EqualTo(1002));
                    else Assert.That(pose, Is.Null);
                }
                Assert.That(PoseReplay.Run(trial, Path.Combine(root, "replay")).classifications_match, Is.True);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
