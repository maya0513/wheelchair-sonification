using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.Tests
{
    public class TrackingAndRecordingTests
    {
        string root;
        [SetUp] public void Setup() => root = Path.Combine(Path.GetTempPath(), "wheelchair-tracking-" + Guid.NewGuid().ToString("N"));
        [TearDown] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

        static TrackingCalibration Calibration(float camber = 20)
        {
            var c = new TrackingCalibration { bodyReady = true, courseReady = true, courseEnd = Vector3.forward * 10,
                chassisReference = Quaternion.Euler(15, 27, 32) };
            for (int i = 0; i < 5; i++) { c.roles[i].key = "tracker-" + i; c.roles[i].runtimeDeviceId = i + 10; }
            foreach (var w in new[] { c.left, c.right })
            {
                float side = w == c.right ? 1 : -1;
                w.center = new Vector3(side * .35f, 0, 0);
                w.axle = Quaternion.AngleAxis(side * camber, Vector3.forward) * Vector3.right;
                w.radialInTracker = Vector3.ProjectOnPlane(Vector3.up, w.axle).normalized;
                w.geometryReady = w.contactReady = true; w.contactRadius = .30f;
            }
            return c;
        }
        static PoseFrame Frame(TrackingCalibration c, int i, float degrees, bool contact = true, float yaw = 0)
        {
            var f = new PoseFrame { time = i * .01 };
            var turn = Quaternion.Euler(0, yaw, 0);
            var body = new Vector3(0, 0, 3 + i * .005f);
            for (int role = 0; role < 5; role++)
            {
                var w = role == 0 || role == 2 ? c.left : c.right;
                var rotation = Quaternion.AngleAxis(degrees, w.axle);
                var radial = rotation * w.radialInTracker;
                Vector3 p = role < 2 ? w.center + radial * (contact ? w.contactRadius : .6f) :
                    role < 4 ? w.center + radial * .12f : Vector3.zero;
                f.poses.Add(new TrackerPose { key = "tracker-" + role, runtimeDeviceId = role + 10,
                    valid = true, acquiredTime = f.time, position = body + turn * p,
                    rotation = role == 4 ? turn * c.chassisReference : turn * rotation });
            }
            return f;
        }
        [TestCase(-20f)] [TestCase(20f)]
        public void FittedWheelPreservesInclinedAxisCenterAndForwardRotation(float camber)
        {
            var axis = Quaternion.AngleAxis(camber, Vector3.forward) * Vector3.right;
            var up = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
            var center = new Vector3(.4f, .1f, -.2f);
            var points = new List<Vector3>(); var rotations = new List<Quaternion>();
            var mount = Quaternion.Euler(21, 33, 5);
            for (int i = 0; i <= 36; i++)
            {
                var r = Quaternion.AngleAxis(i * 10, axis);
                points.Add(center + r * up * .2f); rotations.Add(r * mount);
            }
            var w = CalibrationWizard.FitWheel(points, rotations);
            Assert.That(Vector3.Distance(w.center, center), Is.LessThan(.0001));
            Assert.That(Vector3.Angle(w.axle, axis), Is.LessThan(.01));
            Assert.That(w.sign, Is.EqualTo(1));
            Assert.That(w.fitResidualM, Is.LessThan(.0001));
        }
        [Test]
        public void WheelPhaseIgnoresChassisTranslationYawAndArbitraryTrackerMount()
        {
            var c = Calibration();
            var a = Frame(c, 0, 42); var b = Frame(c, 120, 42, yaw: 95);
            Assert.That(TrackingGeometry.WheelPhase(c, b.poses[4], b.poses[3], c.right),
                Is.EqualTo(TrackingGeometry.WheelPhase(c, a.poses[4], a.poses[3], c.right)).Within(.001));
            var before = Frame(c, 0, 359); var after = Frame(c, 1, 1);
            Assert.That(Mathf.DeltaAngle(TrackingGeometry.WheelPhase(c, before.poses[4], before.poses[3], c.right),
                TrackingGeometry.WheelPhase(c, after.poses[4], after.poses[3], c.right)), Is.EqualTo(2).Within(.001));
        }
        [Test]
        public void WheelAxisFitDoesNotAssumeChairHeadingAlongWorldZ()
        {
            var axis = Quaternion.Euler(0, 90, 0) * (Quaternion.AngleAxis(20, Vector3.forward) * Vector3.right);
            var up = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
            var points = new List<Vector3>(); var rotations = new List<Quaternion>();
            for (int i = 0; i <= 20; i++)
            {
                var r = Quaternion.AngleAxis(i * 5, axis);
                points.Add(r * up * .2f); rotations.Add(r);
            }
            var fitted = CalibrationWizard.FitWheel(points, rotations);
            Assert.That(Vector3.Angle(fitted.axle, axis), Is.LessThan(.02));
            Assert.That(fitted.center.magnitude, Is.LessThan(.0001));
        }
        [Test]
        public void DuplicateRolesReconnectAndExplicitInvalidationPreventValidCalibration()
        {
            var c = Calibration(); var f = Frame(c, 0, 0);
            Assert.That(c.BindingError(f), Is.Null);
            c.roles[0].key = c.roles[1].key;
            Assert.That(c.BindingError(f), Is.Not.Null);
            c.roles[0].key = "tracker-0";
            f.poses[3].runtimeDeviceId++;
            Assert.That(c.Pose(f, TrackerRole.RightWheel), Is.Null);
            Assert.That(c.BindingError(f), Is.Not.Null);
            c.Invalidate("origin_changed"); Assert.That(c.Complete, Is.False);
        }
        [Test]
        public void SyntheticInclinedWheelStrokeIsDetectedAndGapCannotJoinIt()
        {
            var c = Calibration(); var e = new PushEstimator(c, true);
            var values = new List<ExperimentInput>(); e.Received += values.Add;
            for (int i = 0; i <= 70; i++) e.Observe(Frame(c, i, i, i < 70), StudyPhase.Training, StudyTask.Straight);
            var releases = values.Where(v => v.Kind == InputKind.Released).ToArray();
            Assert.That(releases.Length, Is.EqualTo(1));
            Assert.That(releases[0].AngleDegrees, Is.EqualTo(69).Within(.1));
            values.Clear(); e = new PushEstimator(c, true); e.Received += values.Add;
            for (int i = 0; i <= 10; i++) e.Observe(Frame(c, i, i), StudyPhase.Training, StudyTask.Straight);
            var invalid = Frame(c, 11, 11); invalid.poses[3].valid = false;
            e.Observe(invalid, StudyPhase.Training, StudyTask.Straight);
            Assert.That(e.TrackingValid, Is.False); Assert.That(e.Motion, Is.EqualTo(MotionState.Stopped));
            for (int i = 12; i <= 22; i++) e.Observe(Frame(c, i, i, false), StudyPhase.Training, StudyTask.Straight);
            Assert.That(values.Where(v => v.Kind == InputKind.Released).All(v => double.IsNaN(v.AngleDegrees)), Is.True);
        }
        [Test]
        public void BaselineWithoutTargetMeasuresWithoutClassifyingOrPresenting()
        {
            var output = new TestOutput(); var log = new TestLog(); var clock = new TestClock();
            using var e = new ExperimentEngine(clock, output, log, StudyGroup.NoFeedback, null);
            e.Process(ExperimentInput.ChangeContext(0, new ExperimentContext(StudyPhase.Baseline, StudyTask.Straight, MotionState.Straight, true, true)));
            e.Process(ExperimentInput.Contact(0)); e.Process(ExperimentInput.Release(0, 70)); e.Tick();
            Assert.That(log.Records.Count(r => r.Event == "stroke_measured"), Is.EqualTo(1));
            Assert.That(log.Records.Any(r => r.Event == "stroke_classified"), Is.False);
            Assert.That(output.Starts, Is.Empty);
        }
        [Test]
        public void SavedRawPosesReplayTheSameClassificationsAndExportKeepsSources()
        {
            var c = Calibration(); var clock = new FrameDispatchClock(); var output = new TestOutput();
            var settings = new ResearchSessionSettings { participant_id = "P001", phase = "Training", task = "Straight",
                group = "Visual", major_side = "right", target_available = true, target_degrees = 70 };
            var log = new ResearchSessionLog(root, settings, c);
            var estimator = new PushEstimator(c, true);
            using (var engine = new ExperimentEngine(clock, output, log, StudyGroup.Visual, 70))
            {
                estimator.Received += engine.Process;
                for (int i = 1; i <= 75; i++)
                {
                    var f = Frame(c, i, i, i < 71); clock.Now = f.time;
                    log.Poses(f, c); estimator.Observe(f, StudyPhase.Training, StudyTask.Straight); engine.Tick();
                }
                engine.EndSession();
            }
            var report = PoseReplay.Run(log.DirectoryPath, Path.Combine(root, "replay"));
            Assert.That(report.original_classifications, Is.EqualTo(1));
            Assert.That(report.classifications_match, Is.True);
            string original = File.ReadAllText(Path.Combine(log.DirectoryPath, "poses.csv"));
            var archive = SessionExporter.CreateArchive(root, Path.Combine(root, "exports"));
            using (var zip = ZipFile.OpenRead(archive))
            {
                Assert.That(zip.Entries.Any(e => e.Name == "completed.json"), Is.True);
                using var reader = new StreamReader(zip.GetEntry("SHA256SUMS.txt").Open());
                using var sha = SHA256.Create();
                var hash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(original))).Replace("-", "").ToLowerInvariant();
                Assert.That(reader.ReadToEnd(), Does.Contain(hash));
            }
            Assert.That(File.ReadAllText(Path.Combine(log.DirectoryPath, "poses.csv")), Is.EqualTo(original));
            Assert.Throws<InvalidOperationException>(() => SessionExporter.CreateArchive(Path.Combine(root, "missing"), root));
        }
        [Test]
        public void FailedExportAndIncompleteLogDoNotEraseOriginals()
        {
            var log = new ResearchSessionLog(root, new ResearchSessionSettings(), Calibration());
            log.Dispose(); Assert.That(File.Exists(Path.Combine(log.DirectoryPath, "completed.json")), Is.False);
            string blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "a file");
            Assert.Throws<IOException>(() => SessionExporter.CreateArchive(root, blocked));
            Assert.That(File.Exists(Path.Combine(log.DirectoryPath, "poses.csv")), Is.True);
        }
    }
}
