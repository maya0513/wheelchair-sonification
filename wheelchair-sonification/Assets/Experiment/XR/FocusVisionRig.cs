using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace WheelchairSonification.XR
{
    // Actions are enabled before the XR loader is initialized by FocusVisionDemo.Start.
    public sealed class FocusVisionRig : IDisposable
    {
        private readonly List<InputAction> actions = new List<InputAction>();
        private readonly List<(XRRayInteractor ray, LineRenderer line, Transform origin, InputAction tracked)> pointers =
            new List<(XRRayInteractor, LineRenderer, Transform, InputAction)>();
        private readonly InputAction toggle;
        private readonly InputAction recenter;
        private readonly Material lineMaterial;
        public Camera Camera { get; }
        public bool TogglePressed => toggle.WasPressedThisFrame();
        public bool RecenterPressed => recenter.WasPressedThisFrame();
        public bool AnyControllerTracked => pointers.Exists(pointer => pointer.tracked.IsPressed());

        public FocusVisionRig(Transform parent)
        {
            var rig = new GameObject("XR Origin", typeof(XROrigin));
            rig.transform.SetParent(parent, false);
            var offset = new GameObject("Camera offset");
            offset.transform.SetParent(rig.transform, false);
            var cameraObject = new GameObject("XR Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(offset.transform, false);
            cameraObject.tag = "MainCamera";
            Camera = cameraObject.GetComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.clear;
            Camera.nearClipPlane = 0.05f;
            Camera.farClipPlane = 20;
            Camera.allowHDR = false;
            Camera.stereoTargetEye = StereoTargetEyeMask.Both;
            var origin = rig.GetComponent<XROrigin>();
            origin.Camera = Camera;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 0;
            Pose(cameraObject, "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation", "<XRHMD>/trackingState");

            new GameObject("XR Interaction Manager", typeof(XRInteractionManager)).transform.SetParent(parent, false);
            var events = parent.GetComponentInChildren<EventSystem>();
            if (events == null) throw new InvalidOperationException("EventSystemがありません。");
            foreach (var module in events.GetComponents<BaseInputModule>())
            {
                module.enabled = false;
                UnityEngine.Object.Destroy(module);
            }
            var xrModule = events.gameObject.AddComponent<XRUIInputModule>();
            xrModule.enableXRInput = true;
            xrModule.enableMouseInput = Application.isEditor;
            xrModule.enableGamepadInput = false;
            xrModule.enableJoystickInput = false;
            lineMaterial = new Material(Shader.Find("UI/Default"));
            Controller(offset.transform, "LeftHand");
            Controller(offset.transform, "RightHand");
            toggle = Action("Toggle panel", "<XRController>{RightHand}/primaryButton", InputActionType.Button);
            recenter = Action("Recenter panel", "<XRController>{RightHand}/secondaryButton", InputActionType.Button);
            toggle.AddBinding("<XRController>{LeftHand}/primaryButton");
            recenter.AddBinding("<XRController>{LeftHand}/secondaryButton");
        }

        private InputAction Action(string name, string binding, InputActionType type = InputActionType.Value)
        {
            var action = new InputAction(name, type, binding);
            action.Enable();
            actions.Add(action);
            return action;
        }

        private void Pose(GameObject target, string position, string rotation, string tracking)
        {
            var driver = target.AddComponent<TrackedPoseDriver>();
            driver.positionInput = new InputActionProperty(Action(target.name + " position", position));
            driver.rotationInput = new InputActionProperty(Action(target.name + " rotation", rotation));
            driver.trackingStateInput = new InputActionProperty(Action(target.name + " tracking", tracking));
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        }

        private void Controller(Transform parent, string hand)
        {
            var controller = new GameObject(hand + " pointer");
            controller.SetActive(false);
            controller.transform.SetParent(parent, false);
            string binding = "<XRController>{" + hand + "}";
            Pose(controller, binding + "/pointerPosition", binding + "/pointerRotation", binding + "/trackingState");
            var ray = controller.AddComponent<XRRayInteractor>();
            ray.enableUIInteraction = true;
            ray.maxRaycastDistance = 5;
            ray.rayOriginTransform = controller.transform;
            ray.uiPressInput.inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction;
            ray.uiPressInput.inputActionPerformed = Action(hand + " UI press", binding + "/triggerPressed", InputActionType.Button);
            ray.uiPressInput.inputActionValue = Action(hand + " UI value", binding + "/trigger");
            ray.uiScrollInput.inputSourceMode = XRInputValueReader.InputSourceMode.InputAction;
            ray.uiScrollInput.inputAction = Action(hand + " UI scroll", binding + "/thumbstick");
            var line = controller.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMaterial;
            line.positionCount = 2;
            line.startWidth = line.endWidth = 0.0025f;
            line.startColor = line.endColor = new Color(0.1f, 0.8f, 0.8f, 1);
            pointers.Add((ray, line, controller.transform, Action(hand + " tracked", binding + "/isTracked", InputActionType.Button)));
            controller.SetActive(true);
        }

        public void UpdatePointers(bool visible = true)
        {
            foreach (var pointer in pointers)
            {
                bool tracked = visible && pointer.tracked.IsPressed();
                pointer.ray.enabled = tracked;
                pointer.line.enabled = tracked;
                if (!tracked) continue;
                var from = pointer.origin.position;
                var to = from + pointer.origin.forward * 3;
                if (pointer.ray.TryGetCurrentUIRaycastResult(out var hit)) to = hit.worldPosition;
                pointer.line.SetPosition(0, from);
                pointer.line.SetPosition(1, to);
            }
        }

        public static void PrepareDropdown(Dropdown dropdown)
        {
            // uGUI clones a nested Canvas with a normal raycaster when a Dropdown opens.
            var list = dropdown.transform.Find("Dropdown List");
            if (list == null || list.GetComponent<TrackedDeviceGraphicRaycaster>() != null) return;
            var canvas = list.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = dropdown.GetComponentInParent<Canvas>().worldCamera;
            var ordinary = list.GetComponent<GraphicRaycaster>();
            if (ordinary != null) { ordinary.enabled = false; UnityEngine.Object.Destroy(ordinary); }
            list.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }

        public void WorldCanvas(RectTransform rect, Transform parent, Vector3 position, Vector2 size, float scale)
        {
            var canvas = rect.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.localPosition = position;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * scale;
            rect.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            var old = rect.GetComponent<GraphicRaycaster>();
            if (old != null) { old.enabled = false; UnityEngine.Object.Destroy(old); }
            rect.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }

        public void Dispose()
        {
            foreach (var action in actions) { action.Disable(); action.Dispose(); }
            UnityEngine.Object.Destroy(lineMaterial);
        }
    }
}
