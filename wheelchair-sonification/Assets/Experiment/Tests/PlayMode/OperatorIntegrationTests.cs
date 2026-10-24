using System;
using System.IO;
using System.Collections;
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
        GameObject root; ResearchController r; ResearchVisitHost host; string folder;
        [UnitySetUp] public IEnumerator Setup(){folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));root=new GameObject("fixture");var output=root.AddComponent<UnityFeedbackOutput>();r=root.AddComponent<ResearchController>();r.Output=output;r.RootOverride=Path.Combine(folder,"sessions");r.PresentationReady=true;host=root.AddComponent<ResearchVisitHost>();host.RootOverride=Path.Combine(folder,"visits");host.Initialize(r);host.SetInput("synthetic");host.ObserveOperator(true,3);yield return null;}
        OperatorResult Send(string action,string value="",double number=0){var s=host.Snapshot();return host.Execute(new OperatorCommand{id=Guid.NewGuid().ToString("N"),visitId=s.visitId,revision=s.revision,action=action,value=value,number=number});}
        [UnityTearDown] public IEnumerator TearDown(){UnityEngine.Object.Destroy(root);yield return null;if(Directory.Exists(folder))Directory.Delete(folder,true);}
        [UnityTest] public IEnumerator ManualTrialTrainingEndAndSingleTrialExport()
        {bool exampleCancelled=false;host.StopPresentation=()=>exampleCancelled=true;Assert.That(Send("createVisit","P001").ok,Is.True);Send("setGroup","Visual");Assert.That(Send("setSelection","{\"phase\":2,\"task\":2,\"repetition\":1}").ok,Is.True);Assert.That(Send("start").ok,Is.True);yield return new WaitForSecondsRealtime(.08f);Assert.That(exampleCancelled,Is.True);Assert.That(r.Active,Is.True);Assert.That(Send("trainingEnd").ok,Is.True);Assert.That(r.Active,Is.True);Assert.That(r.Output.Last.enabled,Is.False);Assert.That(Send("finishTrial").ok,Is.True);Assert.That(r.Active,Is.False);Assert.That(File.Exists(host.ExportPath),Is.True);Assert.That(Directory.GetDirectories(r.SaveRoot).Length,Is.EqualTo(1));}
        [UnityTest] public IEnumerator DuplicateCommandAndStaleStateCannotCreateSecondTrial()
        {Send("createVisit","P001");var s=host.Snapshot();var command=new OperatorCommand{id="repeat",visitId=s.visitId,revision=s.revision,action="start"};Assert.That(host.Execute(command).ok,Is.True);Assert.That(host.Execute(command).ok,Is.True);Assert.That(Directory.GetDirectories(r.SaveRoot).Length,Is.EqualTo(1));Assert.That(host.Execute(new OperatorCommand{id="stale",visitId=s.visitId,revision=s.revision,action="finishTrial"}).ok,Is.False);yield return null;}
        [UnityTest] public IEnumerator DisconnectStopsAndDoesNotAutoResume()
        {Send("createVisit","P001");Send("start");host.ObserveOperator(true,0);yield return null;yield return null;Assert.That(r.Active,Is.False);host.ObserveOperator(true,3);yield return null;Assert.That(r.Active,Is.False);Assert.That(File.ReadAllText(Path.Combine(r.SaveRoot,r.TrialId,"completed.json")),Does.Contain("\"trial_complete\":false"));}
        [UnityTest] public IEnumerator TransferFailureKeepsSource()
        {Send("createVisit","P001");Send("start");yield return null;Send("finishTrial");Assert.That(host.BeginTransfer(),Is.True);host.TransferFinished(false,"fixture");Assert.That(File.Exists(Path.Combine(r.SaveRoot,r.TrialId,"session.json")),Is.True);Assert.That(host.ExportStatus,Does.Contain("転送失敗"));Assert.That(Send("export").ok,Is.True);}
        [UnityTest] public IEnumerator OlderFailedTrialIsRecoveredBeforeLaterTrial()
        {
            Send("createVisit","P001");Send("start");yield return null;string first=r.TrialId;Send("finishTrial");
            Assert.That(host.BeginTransfer(),Is.True);host.TransferFinished(false,"fixture");
            Send("start");yield return null;string second=r.TrialId;Send("finishTrial");
            Assert.That(host.ExportPath,Is.Null,"A failed transfer must not retry automatically when another trial closes.");
            Assert.That(Send("export").ok,Is.True);using(var zip=ZipFile.OpenRead(host.ExportPath))Assert.That(zip.Entries.Any(e=>e.FullName.Contains(first+"/session.json")),Is.True);
            Assert.That(host.BeginTransfer(),Is.True);host.TransferFinished(true,"fixture");
            using(var zip=ZipFile.OpenRead(host.ExportPath))Assert.That(zip.Entries.Any(e=>e.FullName.Contains(second+"/session.json")),Is.True);
            Assert.That(host.BeginTransfer(),Is.True);host.TransferFinished(true,"fixture");Assert.That(host.ExportPath,Is.Null);
            Assert.That(Send("export").ok,Is.False,"Already verified trials are not copied again during the same app run.");
        }
        [UnityTest] public IEnumerator ClosingAnotherTrialDoesNotOverwritePendingArchive()
        {
            Send("createVisit","P001");Send("start");yield return null;Send("finishTrial");string firstArchive=host.ExportPath;
            Send("start");yield return null;string second=r.TrialId;Send("finishTrial");Assert.That(host.ExportPath,Is.EqualTo(firstArchive));
            Assert.That(host.BeginTransfer(),Is.True);host.TransferFinished(true,"fixture");
            using(var zip=ZipFile.OpenRead(host.ExportPath))Assert.That(zip.Entries.Any(e=>e.FullName.Contains(second+"/session.json")),Is.True);
        }
        [UnityTest] public IEnumerator RestartCanRescanOlderRecordsForExplicitRecovery()
        {
            Send("createVisit","P001");Send("start");yield return null;string first=r.TrialId;Send("finishTrial");
            Assert.That(host.BeginTransfer(),Is.True);host.TransferFinished(true,"fixture");
            UnityEngine.Object.Destroy(host);yield return null;host=root.AddComponent<ResearchVisitHost>();host.RootOverride=Path.Combine(folder,"visits");host.Initialize(r);host.ObserveOperator(true,3);
            Assert.That(host.ExportPath,Is.Null,"Restart must not start recovery automatically.");Assert.That(Send("export").ok,Is.True);
            using(var zip=ZipFile.OpenRead(host.ExportPath))Assert.That(zip.Entries.Any(e=>e.FullName.Contains(first+"/session.json")),Is.True);
        }
        sealed class MissingSource : IPoseSource
        {
            readonly SyntheticPoseSource source=new SyntheticPoseSource(); public bool missing;
            public string Kind=>"synthetic_missing_fixture";
            public PoseFrame Read(double time){var frame=source.Read(time);if(missing)frame.poses[2].valid=false;return frame;}
            public void Dispose()=>source.Dispose();
        }
        [UnityTest] public IEnumerator CriticalTrackerMissingMutesButRecordingContinues()
        {var source=new MissingSource();r.SetSource(source);r.Wizard.UseSynthetic(SyntheticPoseSource.Calibration());Send("createVisit","P001");Send("setGroup","Visual");Send("setSelection","{\"phase\":2,\"task\":2,\"repetition\":1}");Send("start");yield return new WaitForSecondsRealtime(.06f);source.missing=true;yield return new WaitForSecondsRealtime(.04f);Assert.That(r.Active,Is.True);Assert.That(r.Output.Last.enabled,Is.False);Send("finishTrial");}
        sealed class ExportFixture : IExportDestination
        {
            public int calledThread; public string Write(string path,string name){calledThread=System.Threading.Thread.CurrentThread.ManagedThreadId;Assert.That(File.Exists(path),Is.True);return "fixture-export";}
        }
        [UnityTest] public IEnumerator UsbExportUsesWorkerAndStaleAbortStillStops()
        {Send("createVisit","P001");var previous=host.Snapshot();Send("start");yield return null;Assert.That(host.Execute(new OperatorCommand{id="emergency",visitId=previous.visitId,revision=previous.revision,action="abort"}).ok,Is.True);Assert.That(r.Active,Is.False);var export=new ExportFixture();host.UsbDestinationOverride=export;int main=System.Threading.Thread.CurrentThread.ManagedThreadId;Assert.That(Send("exportUsb").ok,Is.True);for(int i=0;i<120&&host.Transferring;i++)yield return null;Assert.That(host.Transferring,Is.False);Assert.That(export.calledThread,Is.Not.EqualTo(main));Assert.That(host.ExportStatus,Is.EqualTo("fixture-export"));}
        [UnityTest] public IEnumerator StorageFailureLeavesUnfinishedData()
        {Send("createVisit","P001");r.RootOverride=Path.Combine(folder,"blocked");Directory.CreateDirectory(folder);File.WriteAllText(r.RootOverride,"blocked");Assert.That(Send("start").ok,Is.False);Assert.That(r.Active,Is.False);yield return null;}
    }
}
