using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WheelchairSonification.Runtime
{
    [Serializable]
    public sealed class PreparationSettings
    {
        public double stillSeconds = 2, minimumTravelMeters = .5, minimumRotationDegrees = 45;
        public double minimumAxisCoherence = .8, minimumValidRatio = .95, motionTimeoutSeconds = 40;
        public double stillSpeedMeters = .04, stillAngularDegrees = 8;
        public double maximumBodyRotationDegrees = 12, minimumWheelSeparationMeters = .2;
        public double maximumAxisForwardDot = .4, maximumReturnAxisAngleDegrees = 20;
    }

    [Serializable]
    public sealed class PreparationCandidate
    {
        public string left, right, body;
        public Vector3 leftAxis, rightAxis, forward;
        public double validRatio, forwardMeters, backwardMeters;
        public bool ready;
        public string reason;
    }

    // Deterministic proposal, never an automatic binding. Ambiguity requires a person to select.
    public static class PreparationInference
    {
        public static PoseFrame Copy(PoseFrame frame)
        {
            var copy = new PoseFrame { time = frame.time };
            foreach (var p in frame.poses) copy.poses.Add(new TrackerPose { key=p.key, product=p.product,
                runtimeDeviceId=p.runtimeDeviceId, valid=p.valid, position=p.position, rotation=p.rotation,
                acquiredTime=p.acquiredTime, inputSystemTime=p.inputSystemTime, invalidReason=p.invalidReason });
            return copy;
        }
        public static PreparationCandidate Identify(IReadOnlyList<PoseFrame> forward, IReadOnlyList<PoseFrame> backward,
            PreparationSettings settings, string left = null, string right = null, string body = null)
        {
            if (forward.Count < 3 || backward.Count < 3) return Failure("前進と後退の記録が足りません。");
            foreach(var key in forward.SelectMany(f=>f.poses).Select(p=>p.key).Distinct())
            {
                var identities=forward.Concat(backward).Select(f=>f.Find(key)).Where(p=>p!=null).Select(p=>p.runtimeDeviceId).Distinct();
                if(identities.Skip(1).Any())return Failure("途中で再接続されました。やり直してください。");
            }
            var keys = forward.SelectMany(f=>f.poses).Select(p=>p.key).Distinct().OrderBy(k=>k,StringComparer.Ordinal).ToArray();
            var found = new List<PreparationCandidate>();
            foreach (string b in keys.Where(k=>body==null||k==body))
            foreach (string l in keys.Where(k=>k!=b&&(left==null||k==left)))
            foreach (string r in keys.Where(k=>k!=b&&k!=l&&(right==null||k==right)))
            {
                var candidate = Fit(forward, backward, settings, l,r,b);
                if (candidate.ready) found.Add(candidate);
            }
            return found.Count == 1 ? found[0] : Failure(found.Count > 1
                ? "複数の候補があります。実物を選んでください。"
                : "割当を決められません。実物を選んでください。");
        }
        static PreparationCandidate Fit(IReadOnlyList<PoseFrame> f, IReadOnlyList<PoseFrame> b, PreparationSettings s, string l,string r,string body)
        {
            var result = Failure("条件不足");
            double ratio = new[] { Ratio(f,l,r,body), Ratio(b,l,r,body) }.Min();
            if (ratio < s.minimumValidRatio) return result;
            var fa = Valid(f,body).ToArray(); var ba = Valid(b,body).ToArray();
            Vector3 move = Vector3.ProjectOnPlane(fa.Last().position-fa.First().position,Vector3.up);
            Vector3 back = Vector3.ProjectOnPlane(ba.Last().position-ba.First().position,Vector3.up);
            if (move.magnitude < s.minimumTravelMeters || back.magnitude < s.minimumTravelMeters || Vector3.Dot(move.normalized,back.normalized)>-.8f) return result;
            if (BodySpread(fa)>s.maximumBodyRotationDegrees || BodySpread(ba)>s.maximumBodyRotationDegrees) return result;
            if (!Axis(f,l,body,s,out var la) || !Axis(f,r,body,s,out var ra) ||
                !Axis(b,l,body,s,out var lb) || !Axis(b,r,body,s,out var rb)) return result;
            if (Vector3.Angle(la,-lb)>s.maximumReturnAxisAngleDegrees || Vector3.Angle(ra,-rb)>s.maximumReturnAxisAngleDegrees) return result;
            var first = fa.First(); Vector3 globalL=first.rotation*la, globalR=first.rotation*ra;
            if (Math.Abs(Vector3.Dot(globalL,move.normalized))>s.maximumAxisForwardDot || Math.Abs(Vector3.Dot(globalR,move.normalized))>s.maximumAxisForwardDot) return result;
            Vector3 rightward=Vector3.Cross(Vector3.up,move.normalized);
            var positions=f.Where(x=>x.Find(l)?.valid==true&&x.Find(r)?.valid==true).Select(x=>x.Find(r).position-x.Find(l).position).ToArray();
            if (positions.Length==0 || positions.Average(p=>Vector3.Dot(p,rightward))<s.minimumWheelSeparationMeters) return result;
            return new PreparationCandidate { ready=true,left=l,right=r,body=body,leftAxis=la,rightAxis=ra,forward=move.normalized,
                validRatio=ratio,forwardMeters=move.magnitude,backwardMeters=back.magnitude,reason="前後進・左右位置・回転軸を確認。実物と照合して確定してください。" };
        }
        static IEnumerable<TrackerPose> Valid(IReadOnlyList<PoseFrame> frames,string key)=>frames.Select(f=>f.Find(key)).Where(p=>p?.valid==true);
        static double BodySpread(TrackerPose[] poses)=>poses.Max(p=>Quaternion.Angle(poses[0].rotation,p.rotation));
        static double Ratio(IReadOnlyList<PoseFrame> frames, params string[] keys)
        {
            var identities=new Dictionary<string,int>();
            foreach(var key in keys){var p=frames.Select(f=>f.Find(key)).FirstOrDefault(p=>p?.valid==true);if(p==null)return 0;identities[key]=p.runtimeDeviceId;}
            return frames.Count(f=>keys.All(k=>{var p=f.Find(k);return p?.valid==true&&p.runtimeDeviceId==identities[k]&&TrackingGeometry.Finite(p.rotation)&&TrackingGeometry.Finite(p.position);}))/(double)frames.Count;
        }
        static bool Axis(IReadOnlyList<PoseFrame> frames,string key,string body,PreparationSettings s,out Vector3 axis)
        {
            Vector3 sum=Vector3.zero;double total=0;PoseFrame previous=null;
            foreach(var f in frames)
            {
                var p=f.Find(key);var b=f.Find(body);
                if(p?.valid!=true||b?.valid!=true){previous=null;continue;}
                if(previous!=null&&f.time>previous.time&&f.time-previous.time<=.11)
                {var v=TrackingGeometry.RotationVector(TrackingGeometry.Relative(previous.Find(body),previous.Find(key)),TrackingGeometry.Relative(b,p));sum+=v;total+=v.magnitude;}
                previous=f;
            }
            axis=sum.normalized;
            return sum.magnitude>=s.minimumRotationDegrees&&total>0&&sum.magnitude/total>=s.minimumAxisCoherence;
        }
        public static PreparationCandidate Failure(string reason)=>new PreparationCandidate{reason=reason};
    }
}
