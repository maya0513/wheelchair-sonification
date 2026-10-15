using System;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.Tests
{
    public class FileSessionLogTests
    {
        private string root;
        [SetUp] public void SetUp() { root = Path.Combine(Path.GetTempPath(), "sonification-tests-" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void TearDown() { if (Directory.Exists(root)) Directory.Delete(root, true); }

        [Test]
        public void CompletedAndInterruptedSessionsAreDistinguishableAndNeverOverwritten()
        {
            string complete;
            using (var file = new FileSessionLog(root, new DemoSessionSettings(), "complete"))
            {
                complete = file.DirectoryPath;
                file.Write(new ExperimentRecord { Event = "session_completed", Context = ExperimentContext.Preparation });
                file.Complete(1);
            }
            string settings = File.ReadAllText(Path.Combine(complete, "session.json"));
            Assert.Throws<IOException>(() => new FileSessionLog(root, new DemoSessionSettings(), "complete"));
            Assert.That(File.ReadAllText(Path.Combine(complete, "session.json")), Is.EqualTo(settings));
            using (var file = new FileSessionLog(root, new DemoSessionSettings(), "interrupted"))
            {
                file.Write(new ExperimentRecord { Event = "contact_started" });
                file.Flush();
            }
            Assert.That(FileSessionLog.CountIncomplete(root), Is.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(root, "interrupted", "events.csv")), Does.Contain("contact_started"));
            Assert.That(File.Exists(Path.Combine(root, "interrupted", "completed.json")), Is.False);
            using (var next = new FileSessionLog(root, new DemoSessionSettings()))
                Assert.That(next.DirectoryPath, Is.Not.EqualTo(complete));
        }

        [Test]
        public void CsvEscapesTextAndUsesInvariantDecimalAndExplicitMissingFields()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                using (var file = new FileSessionLog(root, new DemoSessionSettings()))
                {
                    file.Write(new ExperimentRecord
                    {
                        Sequence = 1, TimeSeconds = 1.25, AngleDegrees = 64.9,
                        Event = "input", Reason = "comma, quote\" and\nnewline", Feedback = FeedbackState.Short
                    });
                    file.Flush();
                    string csv = File.ReadAllText(Path.Combine(file.DirectoryPath, "events.csv"));
                    Assert.That(csv, Does.Contain("1,1.25,,input,0,64.9,SHORT,\"comma, quote\"\" and\nnewline\""));
                    Assert.That(csv, Does.StartWith(FileSessionLog.Header));
                }
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        [Test]
        public void UnusableRootFailsWithoutPretendingThatRecordingStarted()
        {
            Directory.CreateDirectory(root);
            string notADirectory = Path.Combine(root, "file");
            File.WriteAllText(notADirectory, "preserve");
            Assert.Throws<IOException>(() => new FileSessionLog(notADirectory, new DemoSessionSettings()));
            Assert.That(File.ReadAllText(notADirectory), Is.EqualTo("preserve"));
        }

        [Test]
        public void CorruptedOrMismatchedCompletionDoesNotHideAnIncompleteSession()
        {
            using (var file = new FileSessionLog(root, new DemoSessionSettings(), "broken")) { }
            string marker = Path.Combine(root, "broken", "completed.json");
            File.WriteAllText(marker, "{ invalid json");
            Assert.That(FileSessionLog.CountIncomplete(root), Is.EqualTo(1));
            File.WriteAllText(marker, "{\"completed\":true,\"session_id\":\"another_session\"}");
            Assert.That(FileSessionLog.CountIncomplete(root), Is.EqualTo(1));
        }
    }
}
