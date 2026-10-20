using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VIVE.OpenXR.Tracker;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.XR
{
    public sealed class UltimatePoseSource : IPoseSource
    {
        private readonly List<InputAction> actions = new List<InputAction>();
        public UltimatePoseSource()
        {
            // Enable tracker actions BEFORE the OpenXR loader starts, as for HMD/controllers.
            for (int i = 0; i < 5; i++)
                foreach (var control in new[] { "devicePosition", "deviceRotation", "trackingState", "isTracked" })
                {
                    var action = new InputAction("Tracker " + i + " " + control, InputActionType.Value,
                        "<ViveXRTracker>{Ultimate Tracker " + i + "}/" + control);
                    action.Enable(); actions.Add(action);
                }
        }
        public string Kind => "vive_ultimate_tracker";
        public PoseFrame Read(double acquiredTime)
        {
            var frame = new PoseFrame { time = acquiredTime };
            foreach (var device in InputSystem.devices.OfType<ViveXRTracker.XrTrackerDevice>().OrderBy(d => d.description.product, StringComparer.Ordinal))
            {
                double update = device.lastUpdateTime;
                int tracking = device.trackingState.ReadValue();
                // Both position and rotation must be valid. Reject a stale last-known pose.
                bool fresh = update > 0 && InputState.currentTime - update <= .10;
                var position = device.devicePosition.ReadValue();
                var rotation = device.deviceRotation.ReadValue();
                bool valid = device.added && device.isTracked.isPressed && (tracking & 3) == 3 && fresh &&
                    TrackingGeometry.Finite(position) && TrackingGeometry.Finite(rotation);
                frame.poses.Add(new TrackerPose
                {
                    key = device.description.product + "|" + device.description.serial,
                    product = device.description.product, runtimeDeviceId = device.deviceId,
                    position = position, rotation = rotation, valid = valid, acquiredTime = acquiredTime,
                    inputSystemTime = update > 0 ? update : null,
                    invalidReason = valid ? null : !fresh ? "stale_input_state" : "position_or_rotation_not_tracked"
                });
            }
            return frame;
        }
        public void Dispose() { foreach (var action in actions) action.Dispose(); }
    }
}
