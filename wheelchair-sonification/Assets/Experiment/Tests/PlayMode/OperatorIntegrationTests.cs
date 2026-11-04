using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.Tests
{
    public sealed class OperatorIntegrationTests
    {
        GameObject root;
        ResearchController r;
        ResearchVisitHost host;
        string folder;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            root = new GameObject("fixture");
            var output = root.AddComponent<UnityFeedbackOutput>();
            r = root.AddComponent<ResearchController>();
            r.Output = output;
            r.RootOverride = Path.Combine(folder, "sessions");
            r.PresentationReady = true;
            host = root.AddComponent<ResearchVisitHost>();
            host.RootOverride = Path.Combine(folder, "visits");
            host.Initialize(r);
            host.SetInput("synthetic");
            host.ObserveOperator(true, 3);
            yield return null;
        }

        OperatorResult Send(string action, string value = "", double number = 0)
        {
            var s = host.Snapshot();
            return host.Execute(
                new OperatorCommand
                {
                    id = Guid.NewGuid().ToString("N"),
                    visitId = s.visitId,
                    revision = s.revision,
                    action = action,
                    value = value,
                    number = number,
                }
            );
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UnityEngine.Object.Destroy(root);
            yield return null;
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        [UnityTest]
        public IEnumerator ManualTrialTrainingEndAndSingleTrialExport()
        {
            bool exampleCancelled = false;
            host.StopPresentation = () => exampleCancelled = true;
            Assert.That(Send("createVisit", "P001").ok, Is.True);
            Send("setGroup", "Visual");
            Assert.That(
                Send("setSelection", "{\"phase\":2,\"task\":2,\"repetition\":1}").ok,
                Is.True
            );
            Assert.That(Send("start").ok, Is.True);
            yield return new WaitForSecondsRealtime(.08f);
            Assert.That(exampleCancelled, Is.True);
            Assert.That(r.Active, Is.True);
            var saved = JsonUtility.FromJson<ResearchSessionSettings>(
                File.ReadAllText(Path.Combine(r.SaveRoot, r.TrialId, "session.json"))
            );
            Assert.That(saved.feedback.rangeRps, Is.EqualTo(.75));
            Assert.That(Send("trainingEnd").ok, Is.True);
            Assert.That(r.Active, Is.True);
            Assert.That(r.Output.Last.enabled, Is.False);
            Assert.That(Send("finishTrial").ok, Is.True);
            Assert.That(r.Active, Is.False);
            Assert.That(File.Exists(host.ExportPath), Is.True);
            Assert.That(Directory.GetDirectories(r.SaveRoot).Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RepeatedDisplayAlignmentPreservesCalibrationAndSharesGuardWithUi()
        {
            r.enabled = host.enabled = false;
            r.Tick(0);
            var calibration = r.Wizard.Calibration;
            string before = JsonUtility.ToJson(calibration);
            int aligned = 0;
            host.AlignDisplay = () => aligned++;
            void Tick(double time)
            {
                var frame = PreparationInference.Copy(r.Frame);
                frame.time = time;
                host.Preparation.Tick(frame, time, true, r.Settings, r.Volume);
            }
            for (int i = 0; i < 90; i++)
                Tick(i * .025);
            Assert.That(host.Snapshot().canAlignDisplay, Is.True);
            Assert.That(Send("alignDisplay").ok, Is.True);
            Assert.That(Send("alignDisplay").ok, Is.True);
            Assert.That(aligned, Is.EqualTo(2));
            Assert.That(JsonUtility.ToJson(calibration), Is.EqualTo(before));
            Assert.That(r.Wizard.Calibration, Is.SameAs(calibration));
            Assert.That(host.Snapshot().feedbackRangeRps, Is.EqualTo(.75));
            host.ExamplePresenting = () => true;
            Assert.That(host.Snapshot().canAlignDisplay, Is.False);
            Assert.That(host.Snapshot().displayAlignmentReason, Does.Contain("見本"));
            Assert.That(Send("alignDisplay").ok, Is.False);
            host.ExamplePresenting = () => false;
            Assert.That(Send("prep.freeAuditory").ok, Is.True);
            Assert.That(host.Snapshot().canAlignDisplay, Is.False);
            Assert.That(Send("alignDisplay").ok, Is.False);
            Assert.That(Send("prep.cancel").ok, Is.True);
            Assert.That(Send("prep.confirmStopped").ok, Is.True);
            Assert.That(Send("createVisit", "P_ALIGN").ok, Is.True);
            Assert.That(Send("start").ok, Is.True);
            Assert.That(host.Snapshot().canAlignDisplay, Is.False);
            Assert.That(host.Snapshot().displayAlignmentReason, Does.Contain("記録"));
            Assert.That(Send("alignDisplay").ok, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DuplicateCommandAndStaleStateCannotCreateSecondTrial()
        {
            Send("createVisit", "P001");
            var s = host.Snapshot();
            var command = new OperatorCommand
            {
                id = "repeat",
                visitId = s.visitId,
                revision = s.revision,
                action = "start",
            };
            Assert.That(host.Execute(command).ok, Is.True);
            Assert.That(host.Execute(command).ok, Is.True);
            Assert.That(Directory.GetDirectories(r.SaveRoot).Length, Is.EqualTo(1));
            Assert.That(
                host.Execute(
                    new OperatorCommand
                    {
                        id = "stale",
                        visitId = s.visitId,
                        revision = s.revision,
                        action = "finishTrial",
                    }
                ).ok,
                Is.False
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisconnectStopsAndDoesNotAutoResume()
        {
            Send("createVisit", "P001");
            Send("start");
            host.ObserveOperator(true, 0);
            yield return null;
            yield return null;
            Assert.That(r.Active, Is.False);
            host.ObserveOperator(true, 3);
            yield return null;
            Assert.That(r.Active, Is.False);
            Assert.That(
                File.ReadAllText(Path.Combine(r.SaveRoot, r.TrialId, "completed.json")),
                Does.Contain("\"trial_complete\":false")
            );
        }

        [UnityTest]
        public IEnumerator TransferFailureKeepsSource()
        {
            Send("createVisit", "P001");
            Send("start");
            yield return null;
            Send("finishTrial");
            Assert.That(host.BeginTransfer(), Is.True);
            host.TransferFinished(false, "fixture");
            Assert.That(File.Exists(Path.Combine(r.SaveRoot, r.TrialId, "session.json")), Is.True);
            Assert.That(host.ExportStatus, Does.Contain("転送失敗"));
            Assert.That(Send("export").ok, Is.True);
        }

        [UnityTest]
        public IEnumerator OlderFailedTrialIsRecoveredBeforeLaterTrial()
        {
            Send("createVisit", "P001");
            Send("start");
            yield return null;
            string first = r.TrialId;
            Send("finishTrial");
            Assert.That(host.BeginTransfer(), Is.True);
            host.TransferFinished(false, "fixture");
            Send("start");
            yield return null;
            string second = r.TrialId;
            Send("finishTrial");
            Assert.That(
                host.ExportPath,
                Is.Null,
                "A failed transfer must not retry automatically when another trial closes."
            );
            Assert.That(Send("export").ok, Is.True);
            using (var zip = ZipFile.OpenRead(host.ExportPath))
                Assert.That(
                    zip.Entries.Any(e => e.FullName.Contains(first + "/session.json")),
                    Is.True
                );
            Assert.That(host.BeginTransfer(), Is.True);
            host.TransferFinished(true, "fixture");
            using (var zip = ZipFile.OpenRead(host.ExportPath))
                Assert.That(
                    zip.Entries.Any(e => e.FullName.Contains(second + "/session.json")),
                    Is.True
                );
            Assert.That(host.BeginTransfer(), Is.True);
            host.TransferFinished(true, "fixture");
            Assert.That(host.ExportPath, Is.Null);
            Assert.That(
                Send("export").ok,
                Is.False,
                "Already verified trials are not copied again during the same app run."
            );
        }

        [UnityTest]
        public IEnumerator ClosingAnotherTrialDoesNotOverwritePendingArchive()
        {
            Send("createVisit", "P001");
            Send("start");
            yield return null;
            Send("finishTrial");
            string firstArchive = host.ExportPath;
            Send("start");
            yield return null;
            string second = r.TrialId;
            Send("finishTrial");
            Assert.That(host.ExportPath, Is.EqualTo(firstArchive));
            Assert.That(host.BeginTransfer(), Is.True);
            host.TransferFinished(true, "fixture");
            using (var zip = ZipFile.OpenRead(host.ExportPath))
                Assert.That(
                    zip.Entries.Any(e => e.FullName.Contains(second + "/session.json")),
                    Is.True
                );
        }

        [UnityTest]
        public IEnumerator RestartCanRescanOlderRecordsForExplicitRecovery()
        {
            Send("createVisit", "P001");
            Send("start");
            yield return null;
            string first = r.TrialId;
            Send("finishTrial");
            Assert.That(host.BeginTransfer(), Is.True);
            host.TransferFinished(true, "fixture");
            UnityEngine.Object.Destroy(host);
            yield return null;
            host = root.AddComponent<ResearchVisitHost>();
            host.RootOverride = Path.Combine(folder, "visits");
            host.Initialize(r);
            host.ObserveOperator(true, 3);
            Assert.That(host.ExportPath, Is.Null, "Restart must not start recovery automatically.");
            Assert.That(Send("export").ok, Is.True);
            using (var zip = ZipFile.OpenRead(host.ExportPath))
                Assert.That(
                    zip.Entries.Any(e => e.FullName.Contains(first + "/session.json")),
                    Is.True
                );
        }

        sealed class MissingSource : IPoseSource
        {
            readonly SyntheticPoseSource source = new SyntheticPoseSource();
            public bool missing;
            public string Kind => "synthetic_missing_fixture";

            public PoseFrame Read(double time)
            {
                var frame = source.Read(time);
                if (missing)
                    frame.poses[2].valid = false;
                return frame;
            }

            public void Dispose() => source.Dispose();
        }

        [UnityTest]
        public IEnumerator CriticalTrackerMissingMutesButRecordingContinues()
        {
            var source = new MissingSource();
            r.SetSource(source);
            r.Wizard.UseSynthetic(SyntheticPoseSource.Calibration());
            Send("createVisit", "P001");
            Send("setGroup", "Visual");
            Send("setSelection", "{\"phase\":2,\"task\":2,\"repetition\":1}");
            Send("start");
            yield return new WaitForSecondsRealtime(.06f);
            source.missing = true;
            yield return new WaitForSecondsRealtime(.04f);
            Assert.That(r.Active, Is.True);
            Assert.That(r.Output.Last.enabled, Is.False);
            Send("finishTrial");
        }

        sealed class CountingEyes : IEyeTrackingSource
        {
            public int Reads;
            public string Kind => "synthetic-counting-eyes";

            public EyeTrackingFrame Read(double time, int frame)
            {
                Reads++;
                return new EyeTrackingFrame();
            }

            public void Dispose() { }
        }

        [UnityTest]
        public IEnumerator RestDoesNotAcquireEyesAndPreparationCanResumeWithoutWaiting()
        {
            var eyes = new CountingEyes();
            r.EyeSource = eyes;
            Send("createVisit", "P_REST");
            Assert.That(
                Send("setSelection", "{\"phase\":4,\"task\":0,\"repetition\":1}").ok,
                Is.True
            );
            yield return null;
            yield return null;
            Assert.That(eyes.Reads, Is.Zero);
            Assert.That(r.EyeStatus, Is.EqualTo("休憩中"));
            Assert.That(Send("start").ok, Is.False);
            Assert.That(
                Send("setSelection", "{\"phase\":5,\"task\":2,\"repetition\":1}").ok,
                Is.True
            );
            r.CaptureObservation(123, 456);
            Assert.That(eyes.Reads, Is.EqualTo(1));
        }

        sealed class FailedEyes : IEyeTrackingSource
        {
            public string Kind => "synthetic-throwing-eyes";

            public EyeTrackingFrame Read(double time, int frame) =>
                throw new InvalidOperationException("fixture");

            public void Dispose() { }
        }

        [UnityTest]
        public IEnumerator EyeSourceFailureDoesNotStopFeedbackOrRecordingAndFilesAreExported()
        {
            r.EyeSource = new FailedEyes();
            Send("createVisit", "P_EYE");
            Send("setGroup", "Visual");
            Send("setSelection", "{\"phase\":2,\"task\":2,\"repetition\":1}");
            Assert.That(Send("start").ok, Is.True);
            yield return new WaitForSecondsRealtime(.06f);
            Assert.That(r.Active, Is.True);
            Assert.That(r.Output.Last.enabled, Is.True);
            Assert.That(r.EyeStatus, Is.EqualTo("取得不可"));
            Send("finishTrial");
            using var zip = ZipFile.OpenRead(host.ExportPath);
            foreach (var name in new[] { "eyes.csv", "display.csv", "eye-tracking.json" })
                Assert.That(zip.Entries.Any(e => e.FullName.EndsWith("/" + name)), Is.True, name);
        }

        [UnityTest]
        public IEnumerator AuxiliaryStorageFailureStopsAndKeepsIncompleteSource()
        {
            Send("createVisit", "P_EYE_STORAGE");
            Send("start");
            yield return null;
            string path = Path.Combine(r.SaveRoot, r.TrialId);
            // An open writer failing mid-trial must follow the same policy as any other recording failure.
            var field = typeof(ResearchController).GetField(
                "log",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            );
            ((ResearchSessionLog)field.GetValue(r)).EyeLog.Dispose();
            r.CaptureObservation(Time.realtimeSinceStartupAsDouble, 99999);
            Assert.That(r.Active, Is.False);
            Assert.That(File.Exists(Path.Combine(path, "session.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(path, "completed.json")), Is.False);
        }

        sealed class ExportFixture : IExportDestination
        {
            public int calledThread;

            public string Write(string path, string name)
            {
                calledThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                Assert.That(File.Exists(path), Is.True);
                return "fixture-export";
            }
        }

        [UnityTest]
        public IEnumerator UsbExportUsesWorkerAndStaleAbortStillStops()
        {
            Send("createVisit", "P001");
            var previous = host.Snapshot();
            Send("start");
            yield return null;
            Assert.That(
                host.Execute(
                    new OperatorCommand
                    {
                        id = "emergency",
                        visitId = previous.visitId,
                        revision = previous.revision,
                        action = "abort",
                    }
                ).ok,
                Is.True
            );
            Assert.That(r.Active, Is.False);
            var export = new ExportFixture();
            host.UsbDestinationOverride = export;
            int main = System.Threading.Thread.CurrentThread.ManagedThreadId;
            Assert.That(Send("exportUsb").ok, Is.True);
            for (int i = 0; i < 120 && host.Transferring; i++)
                yield return null;
            Assert.That(host.Transferring, Is.False);
            Assert.That(export.calledThread, Is.Not.EqualTo(main));
            Assert.That(host.ExportStatus, Is.EqualTo("fixture-export"));
        }

        [UnityTest]
        public IEnumerator StorageFailureLeavesUnfinishedData()
        {
            Send("createVisit", "P001");
            r.RootOverride = Path.Combine(folder, "blocked");
            Directory.CreateDirectory(folder);
            File.WriteAllText(r.RootOverride, "blocked");
            Assert.That(Send("start").ok, Is.False);
            Assert.That(r.Active, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FreeRunDoesNotWriteAnExistingVisitOrCreateTrialData()
        {
            Assert.That(Send("createVisit", "P001").ok, Is.True);
            var before = Directory
                .GetFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(p => p, File.ReadAllText);
            string group = host.Snapshot().group;
            r.enabled = host.enabled = false;
            r.Tick(0);
            void Tick(double time)
            {
                var frame = PreparationInference.Copy(r.Frame);
                frame.time = time;
                host.Preparation.Tick(frame, time, true, r.Settings, r.Volume);
            }
            Tick(0);
            for (int i = 1; i <= 85; i++)
                Tick(i * .025);
            Assert.That(Send("prep.free").ok, Is.True);
            Assert.That(Send("prep.freeAuditory").ok, Is.True);
            Tick(7.2);
            Assert.That(host.Preparation.Stage, Is.EqualTo("Free"));
            Assert.That(Send("start").ok, Is.False);
            Assert.That(Send("prep.register").ok, Is.False);
            Assert.That(r.Active, Is.False);
            Assert.That(Send("prep.cancel").ok, Is.True);
            for (int i = 1; i <= 90; i++)
                Tick(7.2 + i * .025);
            Assert.That(host.Preparation.Stage, Is.EqualTo("Result"));
            Assert.That(host.Snapshot().group, Is.EqualTo(group));
            Assert.That(host.ExportPath, Is.Null);
            Assert.That(Directory.Exists(r.SaveRoot), Is.False);
            Assert.That(Directory.Exists(host.CheckRoot), Is.False);
            var after = Directory
                .GetFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(p => p, File.ReadAllText);
            CollectionAssert.AreEquivalent(before, after);
            Assert.That(Send("start").ok, Is.True);
            Assert.That(r.Active, Is.True);
            Assert.That(host.Preparation.Mode, Is.Empty);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StandalonePreparationIgnoresPcLeaseButCannotOverlapResearch()
        {
            Send("createVisit", "P001");
            var before = host.Snapshot();
            var start = new OperatorCommand
            {
                id = "preparation-once",
                visitId = before.visitId,
                revision = before.revision,
                action = "prep.start",
            };
            Assert.That(host.Execute(start).ok, Is.True);
            Assert.That(host.Execute(start).ok, Is.True);
            Assert.That(Directory.GetDirectories(host.CheckRoot).Length, Is.EqualTo(1));
            host.ObserveOperator(true, 0);
            yield return null;
            yield return null;
            Assert.That(host.Preparation.Active, Is.True);
            Assert.That(r.Active, Is.False);
            Assert.That(Send("start").ok, Is.False);
            Assert.That(Send("setInput", "synthetic").ok, Is.False);
            Assert.That(
                host.Execute(
                    new OperatorCommand
                    {
                        id = "old",
                        visitId = before.visitId,
                        revision = before.revision,
                        action = "prep.next",
                    }
                ).ok,
                Is.False
            );
            Assert.That(
                host.Execute(
                    new OperatorCommand
                    {
                        id = "cancel-priority",
                        visitId = "stale-visit",
                        revision = -1,
                        action = "prep.cancel",
                    }
                ).ok,
                Is.True
            );
            Assert.That(host.Preparation.Active, Is.False);
            Assert.That(host.Snapshot().schema, Is.EqualTo(3));
            using var zip = ZipFile.OpenRead(host.ExportPath);
            Assert.That(
                zip.Entries.Any(e =>
                    e.FullName.EndsWith("closed.json") && e.FullName.StartsWith("DeviceChecks/")
                ),
                Is.True
            );
        }
    }
}
