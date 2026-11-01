using System;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using VIVE.OpenXR;
using VIVE.OpenXR.EyeTracker;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.XR
{
    public sealed class FocusVisionEyeSource : IEyeTrackingSource
    {
        public string Kind => "vive_eye_tracker_poll";

        public EyeTrackingFrame Read(double acquiredTime, int frame)
        {
            var f = new EyeTrackingFrame { time = acquiredTime, frame = frame };
#if UNITY_ANDROID && !UNITY_EDITOR
            var feature = OpenXRSettings.Instance?.GetFeature<ViveEyeTracker>();
            if (
                feature == null
                || !feature.enabled
                || !OpenXRRuntime.IsExtensionEnabled(ViveEyeTracker.kOpenxrExtensionString)
            )
            {
                f.reason = "eye_extension_unavailable";
                return f;
            }
            try
            {
                if (feature.GetEyeGazeData(out var gaze) && gaze != null && gaze.Length >= 2)
                {
                    f.gazeApiAvailable = true;
                    for (int i = 0; i < 2; i++)
                    {
                        var e = i == 0 ? f.left : f.right;
                        e.position = gaze[i].gazePose.position.ToUnityVector();
                        e.rotation = gaze[i].gazePose.orientation.ToUnityQuaternion();
                        e.gazeValid =
                            gaze[i].isValid
                            && TrackingGeometry.Finite(e.position)
                            && TrackingGeometry.Finite(e.rotation);
                    }
                }
                else
                    f.reason = "gaze_api_unavailable";
            }
            catch (Exception)
            {
                f.reason = "gaze_api_exception";
            }
            try
            {
                if (feature.GetEyePupilData(out var pupils) && pupils != null && pupils.Length >= 2)
                {
                    f.pupilApiAvailable = true;
                    for (int i = 0; i < 2; i++)
                    {
                        var e = i == 0 ? f.left : f.right;
                        var p = pupils[i];
                        e.pupilDiameter = p.pupilDiameter;
                        e.pupilX = p.pupilPosition.x;
                        e.pupilY = p.pupilPosition.y;
                        e.pupilDiameterValid = p.isDiameterValid && float.IsFinite(e.pupilDiameter);
                        e.pupilPositionValid =
                            p.isPositionValid
                            && float.IsFinite(e.pupilX)
                            && float.IsFinite(e.pupilY);
                    }
                }
            }
            catch (Exception)
            {
                f.reason += (string.IsNullOrEmpty(f.reason) ? "" : ";") + "pupil_api_exception";
            }
            try
            {
                if (
                    feature.GetEyeGeometricData(out var geometry)
                    && geometry != null
                    && geometry.Length >= 2
                )
                {
                    f.geometryApiAvailable = true;
                    for (int i = 0; i < 2; i++)
                    {
                        var e = i == 0 ? f.left : f.right;
                        var g = geometry[i];
                        e.openness = g.eyeOpenness;
                        e.wide = g.eyeWide;
                        e.squeeze = g.eyeSqueeze;
                        e.geometryValid =
                            g.isValid
                            && float.IsFinite(e.openness)
                            && float.IsFinite(e.wide)
                            && float.IsFinite(e.squeeze);
                    }
                }
            }
            catch (Exception)
            {
                f.reason += (string.IsNullOrEmpty(f.reason) ? "" : ";") + "geometry_api_exception";
            }
            if (!f.left.gazeValid || !f.right.gazeValid)
                f.reason += (string.IsNullOrEmpty(f.reason) ? "" : ";") + "invalid_eye_gaze";
#else
            f.reason = "eye_tracking_requires_focus_vision";
#endif
            return f;
        }

        public void Dispose() { } // OpenXR feature owns the native tracker lifecycle.
    }
}
