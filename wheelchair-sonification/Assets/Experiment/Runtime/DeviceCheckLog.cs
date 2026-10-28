using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WheelchairSonification.Runtime
{
    public sealed class DeviceCheckLog : IDisposable
    {
        public string DirectoryPath { get; }
        StreamWriter poses,operations,speeds; double flushAt;
        public DeviceCheckLog(string root,string id,PreparationSettings settings,string source)
        {
            FileStorage.EnsureReserve(root); DirectoryPath=Path.Combine(root,id);Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(Path.Combine(DirectoryPath,"settings.json"),JsonUtility.ToJson(settings,true));
            File.WriteAllText(Path.Combine(DirectoryPath,"build.json"),JsonUtility.ToJson(new CheckIdentity{input=source,version=Application.version,unity=Application.unityVersion,commit=Resources.Load<TextAsset>("BuildIdentity")?.text??"development_worktree",createdUtc=DateTime.UtcNow.ToString("O")},true));
            try
            {
                poses=new StreamWriter(Path.Combine(DirectoryPath,"poses.csv"));operations=new StreamWriter(Path.Combine(DirectoryPath,"operations.csv"));speeds=new StreamWriter(Path.Combine(DirectoryPath,"speeds.csv"));
                poses.WriteLine("acquired_time_s,input_system_time_s,role,key,runtime_device_id,tracking_valid,x_m,y_m,z_m,qx,qy,qz,qw,invalid_reason");
                operations.WriteLine("software_time_s,action,value");
                speeds.WriteLine("software_time_s,stage,valid,fresh,left_rps,right_rps,reason");
            }catch{Dispose();throw;}
        }
        [Serializable] sealed class CheckIdentity{public string input,version,unity,commit,createdUtc;public bool physical_accuracy_verified=false;}
        public void Pose(PoseFrame frame,TrackingCalibration calibration)
        {
            foreach(var p in frame.poses)
            {
                int role=Array.FindIndex(calibration.roles,r=>r.key==p.key);bool valid=p.valid&&TrackingGeometry.Finite(p.position)&&TrackingGeometry.Finite(p.rotation);
                poses.WriteLine(string.Join(",",new[]{ResearchSessionLog.N(frame.time),ResearchSessionLog.N(p.inputSystemTime),role<0?"Unassigned":((TrackerRole)role).ToString(),ResearchSessionLog.E(p.key),p.runtimeDeviceId.ToString(),valid?"true":"false",
                    valid?ResearchSessionLog.N(p.position.x):"",valid?ResearchSessionLog.N(p.position.y):"",valid?ResearchSessionLog.N(p.position.z):"",valid?ResearchSessionLog.N(p.rotation.x):"",valid?ResearchSessionLog.N(p.rotation.y):"",valid?ResearchSessionLog.N(p.rotation.z):"",valid?ResearchSessionLog.N(p.rotation.w):"",ResearchSessionLog.E(p.invalidReason)}));
            }
            if(frame.time>=flushAt){FileStorage.EnsureReserve(DirectoryPath);poses.Flush();operations.Flush();speeds.Flush();flushAt=frame.time+.5;}
        }
        public void Speed(double time,string stage,WheelSpeedSample sample)=>speeds.WriteLine(string.Join(",",new[]{ResearchSessionLog.N(time),stage,sample.valid.ToString(),sample.fresh.ToString(),sample.valid?ResearchSessionLog.N(sample.leftRps):"",sample.valid?ResearchSessionLog.N(sample.rightRps):"",ResearchSessionLog.E(sample.reason)}));
        public void Action(double time,string name,string value)=>operations?.WriteLine(ResearchSessionLog.N(time)+","+ResearchSessionLog.E(name)+","+ResearchSessionLog.E(value));
        public void Save(PreparationSnapshot snapshot,TrackingCalibration calibration)
        {Atomic("report.json",JsonUtility.ToJson(snapshot,true));Atomic("calibration.json",JsonUtility.ToJson(calibration,true));}
        void Atomic(string name,string text){var file=Path.Combine(DirectoryPath,name);File.WriteAllText(file+".partial",text);if(File.Exists(file))File.Delete(file);File.Move(file+".partial",file);}
        public void Dispose(){try{poses?.Dispose();}finally{try{operations?.Dispose();}finally{speeds?.Dispose();poses=null;operations=null;speeds=null;}}}
    }
}
