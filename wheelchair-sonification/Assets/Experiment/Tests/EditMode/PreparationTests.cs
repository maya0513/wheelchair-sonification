using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.Tests
{
    // Consistent vehicle translation, wheel centres, cambered axle rotation, eccentric mounting, and nonrigid wrists.
    public static class ChairFixture
    {
        public static readonly Quaternion BodyMount=Quaternion.Euler(21,33,-17);
        public static PoseFrame Frame(double t,double distance,double leftTurns,double rightTurns,bool wrists=true,float heading=37)
        {
            var frame=new PoseFrame{time=t};var yaw=Quaternion.Euler(0,heading,0);var body=yaw*new Vector3(0,0,(float)distance);
            foreach(int role in new[]{4,3,0,2,1})
            {
                if(role<2&&!wrists)continue;
                bool wheel=role==2||role==3;var axis=Quaternion.Euler(0,0,role==2?-12:12)*Vector3.right;
                var spin=wheel?Quaternion.AngleAxis((float)((role==2?leftTurns:rightTurns)*360),axis):Quaternion.identity;
                var mount=role==4?BodyMount:Quaternion.Euler(role*19,role*31,role*-13);
                var centre=role==2?new Vector3(-.38f,.32f,0):role==3?new Vector3(.38f,.32f,0):role==4?new Vector3(.07f,.85f,-.15f):new Vector3(role==0?-.25f:.25f,.8f,.2f);
                Vector3 position=body+yaw*(centre+(wheel?spin*new Vector3(0,.11f,.03f):Vector3.zero));
                var rotation=yaw*spin*mount;
                if(role<2){position+=yaw*new Vector3(0,(float)Math.Sin(t*5)*.12f,(float)Math.Sin(t*3)*.19f);rotation*=Quaternion.Euler((float)Math.Sin(t*5)*24,(float)Math.Cos(t*3)*35,0);}
                frame.poses.Add(new TrackerPose{key="fixture-"+role,product="KINEMATIC FIXTURE",runtimeDeviceId=100+role,position=position,rotation=rotation,valid=true,acquiredTime=t,inputSystemTime=t});
            }
            return frame;
        }
        public static List<PoseFrame> Segment(bool backwards,bool wrists=true,float heading=37)
        {var list=new List<PoseFrame>();for(int i=0;i<=80;i++){double d=backwards?1-i/80.0:i/80.0;list.Add(Frame((backwards?10:1)+i*.025,d,d/(2*Math.PI*.3),d/(2*Math.PI*.3),wrists,heading));}return list;}
    }
    public sealed class PreparationTests
    {
        [TestCase(false)][TestCase(true)] public void InfersThreeOrFiveDevicesWithIndependentMountingAndCamber(bool wrists)
        {
            var result=PreparationInference.Identify(ChairFixture.Segment(false,wrists),ChairFixture.Segment(true,wrists),new PreparationSettings());
            Assert.That(result.ready,Is.True,result.reason);Assert.That(result.body,Is.EqualTo("fixture-4"));Assert.That(result.left,Is.EqualTo("fixture-2"));Assert.That(result.right,Is.EqualTo("fixture-3"));
            var expected=Quaternion.Inverse(ChairFixture.BodyMount)*(Quaternion.Euler(0,0,-12)*Vector3.right);
            Assert.That(Vector3.Angle(result.leftAxis,expected),Is.LessThan(.1));
        }
        [Test] public void DeviceOrderAndHeadingDoNotDetermineRoles()
        {
            var a=ChairFixture.Segment(false,true,-120);var b=ChairFixture.Segment(true,true,-120);foreach(var f in a.Concat(b))f.poses.Reverse();
            var result=PreparationInference.Identify(a,b,new PreparationSettings());Assert.That(result.ready,Is.True,result.reason);Assert.That(result.left,Is.EqualTo("fixture-2"));
        }
        [Test] public void AmbiguousRigidDuplicateIsNeverConfirmed()
        {
            var a=ChairFixture.Segment(false,false);var b=ChairFixture.Segment(true,false);
            foreach(var f in a.Concat(b)){var p=PreparationInference.Copy(f).Find("fixture-4");p.key="duplicate";f.poses.Add(p);}
            var result=PreparationInference.Identify(a,b,new PreparationSettings());Assert.That(result.ready,Is.False);Assert.That(result.reason,Does.Contain("複数"));
        }
        [Test] public void MissingReconnectedAndInsufficientMotionAreRejected()
        {
            var a=ChairFixture.Segment(false,false);var b=ChairFixture.Segment(true,false);
            foreach(var f in a.Take(20))f.Find("fixture-2").valid=false;
            Assert.That(PreparationInference.Identify(a,b,new PreparationSettings()).ready,Is.False);
            a=ChairFixture.Segment(false,false);foreach(var f in a.Skip(40))f.Find("fixture-2").runtimeDeviceId++;
            Assert.That(PreparationInference.Identify(a,b,new PreparationSettings()).ready,Is.False);
            a=ChairFixture.Segment(false,false).Take(20).ToList();Assert.That(PreparationInference.Identify(a,b,new PreparationSettings()).ready,Is.False);
        }
        [Test] public void ForcedLeftRightSwapAndWrongReturnAreRejected()
        {
            Assert.That(PreparationInference.Identify(ChairFixture.Segment(false,false),ChairFixture.Segment(true,false),new PreparationSettings(),"fixture-3","fixture-2","fixture-4").ready,Is.False);
            Assert.That(PreparationInference.Identify(ChairFixture.Segment(false,false),ChairFixture.Segment(false,false),new PreparationSettings()).ready,Is.False);
        }
        string folder;PreparationWorkflow workflow;double time,distance,lt,rt;BilateralFeedback output;
        [SetUp] public void Setup()
        {folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));time=distance=lt=rt=0;workflow=new PreparationWorkflow(new CalibrationWizard(),()=>folder,()=>"synthetic_kinematic_fixture",v=>output=v,()=>output=default);Tick();}
        [TearDown] public void Cleanup(){workflow.Dispose();if(Directory.Exists(folder))Directory.Delete(folder,true);}
        void Tick(){workflow.Tick(ChairFixture.Frame(time,distance,lt,rt,false),time,true,new FeedbackSettings(),.1f);}
        void Hold(double seconds){int n=(int)Math.Ceiling(seconds/.025);for(int i=0;i<n;i++){time+=.025;Tick();}}
        void Move(double endDistance,double left,double right)
        {double start=distance,sl=lt,sr=rt;for(int i=1;i<=80;i++){time+=.025;distance=start+(endDistance-start)*i/80;lt=sl+(left-sl)*i/80;rt=sr+(right-sr)*i/80;Tick();}Hold(2.3);}
        void Ready(string stage){Assert.That(workflow.Stage,Is.EqualTo(stage));workflow.Execute("next");Hold(5.1);}
        void Calibrate()
        {
            workflow.Execute("start");Hold(2.3);Ready("ForwardReady");Move(1,.6,.6);Ready("BackwardReady");Move(0,0,0);
            Assert.That(workflow.Stage,Is.EqualTo("Candidate"),workflow.View.problem);workflow.Execute("next");Assert.That(workflow.Stage,Is.EqualTo("WristsChoice"));workflow.Execute("skip");
        }
        [Test] public void StandaloneWholeCheckUsesVirtualClockAndRetainsSeparateRecords()
        {
            Calibrate();Ready("VerifyReady");Hold(2.3);Ready("VerifyForwardReady");Move(1,.6,.6);Ready("VerifyBackwardReady");Move(0,0,0);
            Ready("VerifyLeftReady");Move(.3,.1,.3);Ready("VerifyRightReady");Move(.6,.4,.4);
            Assert.That(workflow.Stage,Is.EqualTo("VisualReady"));workflow.Execute("next");Hold(1);Assert.That(output.enabled,Is.True);Assert.That(output.group,Is.EqualTo(StudyGroup.Visual));Hold(7);
            Assert.That(workflow.Stage,Is.EqualTo("VisualAnswer"));Assert.That(output.enabled,Is.False);workflow.Execute("yes");workflow.Execute("next");Hold(8);workflow.Execute("no");
            Assert.That(workflow.Stage,Is.EqualTo("Result"));Assert.That(workflow.Active,Is.False);Assert.That(workflow.View.results.Single(r=>r.id=="audio").status,Is.EqualTo("attention"));
            Assert.That(workflow.View.physicalAccuracyVerified,Is.False);Assert.That(File.Exists(Path.Combine(workflow.View.directory,"poses.csv")),Is.True);
            var zip=SessionExporter.CreateCheckArchive(workflow.View.directory,Path.Combine(folder,"exports"));using var archive=ZipFile.OpenRead(zip);Assert.That(archive.Entries.Any(e=>e.FullName.StartsWith("DeviceChecks/")&&e.FullName.EndsWith("report.json")),Is.True);Assert.That(archive.Entries.Any(e=>e.FullName=="SHA256SUMS.txt"),Is.True);
        }
        [Test] public void LocalCancelStoresIncompleteChecksWithoutResearchTrial()
        {workflow.Execute("start");Hold(.2);workflow.Execute("cancel");Assert.That(workflow.Stage,Is.EqualTo("Result"));Assert.That(workflow.View.problem,Does.Contain("中断"));Assert.That(File.Exists(Path.Combine(workflow.View.directory,"report.json")),Is.True);Assert.That(File.Exists(Path.Combine(workflow.View.directory,"session.json")),Is.False);}
        [Test] public void ManualSelectionRejectsDuplicatesAndRequiresFreshMotion()
        {workflow.Execute("start");Hold(2.3);workflow.Execute("manual");workflow.Execute("select","fixture-2",2);Assert.Throws<InvalidOperationException>(()=>workflow.Execute("select","fixture-2",3));workflow.Execute("select","fixture-3",3);workflow.Execute("select","fixture-4",4);Assert.That(workflow.Stage,Is.EqualTo("ForwardReady"));Assert.That(workflow.View.results.Single(r=>r.id=="axes").status,Is.EqualTo("pending"));}
        [Test] public void InterruptionCannotAutoResumeAndDoesNotRequireAController()
        {Calibrate();Ready("VerifyReady");workflow.Interrupt("fixture disconnect");Hold(3);Assert.That(workflow.Stage,Is.EqualTo("Paused"));Hold(20*60);Assert.That(workflow.Stage,Is.EqualTo("Paused"));Assert.That(output.enabled,Is.False);}
        [Test] public void SavingFailureCannotProduceASuccessfulResult()
        {workflow.Execute("start");string path=workflow.View.directory;Directory.Move(path,path+"-original");File.WriteAllText(path,"blocked");Hold(1);Assert.That(workflow.Stage,Is.EqualTo("Result"));Assert.That(workflow.View.results.Single(r=>r.id=="storage").status,Is.EqualTo("attention"));Assert.That(output.enabled,Is.False);}
        [Test] public void OutputRecheckHasItsOwnScopeAndDoesNotRepeatMovement()
        {workflow.Execute("recheck","audio");Assert.That(workflow.Stage,Is.EqualTo("AudioReady"));workflow.Execute("next");Hold(8);workflow.Execute("yes");Assert.That(workflow.Stage,Is.EqualTo("Result"));Assert.That(workflow.View.scope,Is.EqualTo("audio"));Assert.That(workflow.View.results.Single(r=>r.id=="roles").status,Is.EqualTo("not_in_scope"));Assert.That(workflow.View.results.Single(r=>r.id=="audio").status,Is.EqualTo("confirmed"));}
        [Test] public void MotionRecheckRequiresAnExistingCalibration()
        {Assert.Throws<InvalidOperationException>(()=>workflow.Execute("recheck","left"));Calibrate();workflow.Execute("cancel");workflow.Execute("recheck","left");Assert.That(workflow.Stage,Is.EqualTo("VerifyLeftReady"));}
        [Test] public void PausedCheckCanRetryWithoutLosingItsOriginalRecord()
        {workflow.Execute("start");Hold(2.3);workflow.Interrupt("fixture pause");Hold(2.3);Assert.That(workflow.Stage,Is.EqualTo("Paused"));string first=workflow.View.directory;workflow.Execute("retry");Assert.That(workflow.Stage,Is.EqualTo("Still"));Assert.That(workflow.View.directory,Is.Not.EqualTo(first));Assert.That(File.Exists(Path.Combine(first,"closed.json")),Is.True);}
        [Test] public void SettingsRejectInvalidValuesAndInvalidatePriorCalibration()
        {Assert.Throws<ArgumentException>(()=>workflow.Execute("settings","{\"minimumValidRatio\":5}"));workflow.Execute("settings","default");Assert.That(workflow.Stage,Is.EqualTo("Home"));Assert.That(workflow.Settings.minimumValidRatio,Is.EqualTo(.95));}
        [Test] public void EndedPoseSourceStopsInsteadOfReusingLastKnownPositions()
        {workflow.Execute("start");workflow.Tick(null,time+.1,true,new FeedbackSettings(),.1f);Assert.That(workflow.Stage,Is.EqualTo("Stopping"));Assert.That(workflow.View.problem,Does.Contain("位置姿勢"));Assert.That(output.enabled,Is.False);}
    }
}
