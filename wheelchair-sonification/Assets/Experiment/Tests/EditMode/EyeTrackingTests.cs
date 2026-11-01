using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.Tests
{
    public sealed class EyeTrackingTests
    {
        [Test]
        public void AnchorPreservesPlacementAcrossArbitraryMountRotationAndBodyTurn()
        {
            var source = new SyntheticPoseSource();
            var frame = source.Read(0);
            var c = SyntheticPoseSource.Calibration();
            var body = c.Pose(frame, TrackerRole.Chassis);
            body.rotation = Quaternion.Euler(23, 64, -17);
            var anchor = new FeedbackDisplayAnchor();
            var head = new Vector3(0, 1, 0);
            anchor.Align(frame, c, head, Quaternion.identity);
            var before = anchor.Pose(frame, c);
            Assert.That(
                Vector3.Distance(
                    before.position,
                    head + new Vector3(0, -Mathf.Tan(12 * Mathf.Deg2Rad) * 1.4f, 1.4f)
                ),
                Is.LessThan(.00001)
            );
            var old = body.position;
            var turn = Quaternion.Euler(0, 90, 0);
            body.position += new Vector3(2, 0, 1);
            body.rotation = turn * body.rotation;
            var after = anchor.Pose(frame, c);
            Assert.That(
                Vector3.Distance(after.position, body.position + turn * (before.position - old)),
                Is.LessThan(.00001)
            );
            body.runtimeDeviceId++;
            Assert.That(anchor.IsValid(frame, c), Is.False);
        }

        [Test]
        public void NewCalibrationAndInvalidationCannotReuseOldDisplayAnchor()
        {
            var s = new SyntheticPoseSource();
            var frame = s.Read(0);
            var c = SyntheticPoseSource.Calibration();
            var a = new FeedbackDisplayAnchor();
            a.Align(frame, c, Vector3.zero, Quaternion.identity);
            Assert.That(a.IsValid(frame, SyntheticPoseSource.Calibration()), Is.False);
            c.Invalidate("origin_changed");
            Assert.That(a.IsValid(frame, c), Is.False);
        }

        [Test]
        public void LogsKeepInvalidEyesBlankAndIncludeContextAndQuality()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                using (var log = new EyeTrackingLog(folder, "synthetic-test", true))
                {
                    var f = new EyeTrackingFrame { time = 1, frame = 9 };
                    f.left.gazeValid = true;
                    f.left.position = new Vector3(1, 2, 3);
                    log.Write(
                        f,
                        new DisplayObservation
                        {
                            headValid = true,
                            headPosition = new Vector3(4, 5, 6),
                            anchorValid = true,
                            visible = true,
                            left = .2,
                            right = .8,
                        }
                    );
                    log.Write(
                        new EyeTrackingFrame
                        {
                            time = 1.02,
                            frame = 10,
                            reason = "missing",
                        },
                        new DisplayObservation()
                    );
                    log.Flush();
                    Assert.That(log.Quality.leftValidRatio, Is.EqualTo(.5));
                    Assert.That(log.Quality.rightValidRatio, Is.Zero);
                    Assert.That(log.Quality.meanIntervalSeconds, Is.EqualTo(.02).Within(.000001));
                }
                var rows = File.ReadAllLines(Path.Combine(folder, "eyes.csv"));
                Assert.That(rows.Length, Is.EqualTo(5));
                Assert.That(rows[1], Does.StartWith("1,9,,left,true,1,2,3"));
                Assert.That(rows[2], Does.StartWith("1,9,,right,false,,,,,,,,,,"));
                Assert.That(
                    File.ReadAllText(Path.Combine(folder, "eye-tracking.json")),
                    Does.Contain("synthetic-test")
                );
                Assert.That(File.Exists(Path.Combine(folder, "display.json")), Is.True);
                foreach (var row in rows)
                    Assert.That(row.Split(',').Length, Is.EqualTo(33));
                foreach (var row in File.ReadAllLines(Path.Combine(folder, "display.csv")))
                    Assert.That(row.Split(',').Length, Is.EqualTo(20));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void NonFiniteValuesCannotBeMarkedAsValid()
        {
            var f = new EyeTrackingFrame();
            f.left.gazeValid = true;
            f.left.position.x = float.NaN;
            f.right.pupilDiameterValid = true;
            f.right.pupilDiameter = float.PositiveInfinity;
            f.Sanitize();
            Assert.That(f.left.gazeValid, Is.False);
            Assert.That(f.right.pupilDiameterValid, Is.False);
        }

        [TestCase("{\"saved\":true,\"sha256\":\"expected\"}", true)]
        [TestCase("{\"saved\":false,\"sha256\":\"expected\"}", false)]
        [TestCase("{\"saved\":true,\"sha256\":\"wrong\"}", false)]
        [TestCase("null", false)]
        [TestCase("not-json", false)]
        public void RecoveryReceiptRequiresAValidMatchingAcknowledgement(string text, bool expected)
        {
            Assert.That(OperatorLink.ReceiptMatches(text, "expected"), Is.EqualTo(expected));
        }

        [Test]
        public void AllFeedbackSettingsRejectNonFiniteConfiguration()
        {
            foreach (var field in typeof(WheelchairSonification.Core.FeedbackSettings).GetFields())
            foreach (
                var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }
            )
            {
                var settings = new WheelchairSonification.Core.FeedbackSettings();
                field.SetValue(settings, value);
                Assert.Throws<ArgumentException>(() => settings.Validate(), field.Name);
            }
        }
    }
}
