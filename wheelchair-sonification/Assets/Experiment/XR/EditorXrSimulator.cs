#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace WheelchairSonification.Editor
{
    public sealed class EditorXrSimulator : MonoBehaviour
    {
        private readonly List<InputAction> actions = new List<InputAction>();
        private WheelchairSonification.XR.FocusVisionDemo owner;
        public void Initialize(Transform camera)
        {
            owner = camera.GetComponentInParent<WheelchairSonification.XR.FocusVisionDemo>();
            var host = new GameObject("Unity XR Interaction Simulator (explicit Editor preview)");
            host.SetActive(false); host.transform.SetParent(transform);
            var simulator = host.AddComponent<XRInteractionSimulator>();
            // The sample prefab normally supplies these readers. Programmatic setup must also fill unused readers.
            foreach (var p in typeof(XRInteractionSimulator).GetProperties())
            {
                if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length != 0) continue;
                if (p.PropertyType == typeof(XRInputButtonReader) && p.GetValue(simulator) == null)
                    p.SetValue(simulator, new XRInputButtonReader(p.Name));
                if (p.PropertyType == typeof(XRInputValueReader<Vector2>) && p.GetValue(simulator) == null)
                    p.SetValue(simulator, new XRInputValueReader<Vector2>(p.Name));
            }
            simulator.cameraTransform = camera;
            Value(simulator.translateXInput, Axis("Translate X", "<Keyboard>/a", "<Keyboard>/d"));
            Value(simulator.translateYInput, Axis("Translate Y", "<Keyboard>/q", "<Keyboard>/e"));
            Value(simulator.translateZInput, Axis("Translate Z", "<Keyboard>/s", "<Keyboard>/w"));
            Value(simulator.mouseRotationDeltaInput, Action("Mouse look", "<Mouse>/delta", InputActionType.Value));
            simulator.toggleMouseInput = Button("Toggle mouse look", "<Keyboard>/space");
            simulator.toggleManipulateHeadInput = Button("Manipulate head", "<Keyboard>/h");
            simulator.toggleManipulateLeftInput = Button("Manipulate left", "<Keyboard>/leftBracket");
            simulator.toggleManipulateRightInput = Button("Manipulate right", "<Keyboard>/rightBracket");
            simulator.triggerInput = Button("Trigger", "<Mouse>/leftButton");
            simulator.resetInput = Button("Reset simulator", "<Keyboard>/backspace");
            simulator.targetedDeviceInput = TargetedDevices.HMD;
            host.SetActive(true);
        }
        private void Update()
        {
            if (owner == null || Keyboard.current == null) return;
            if (Keyboard.current.mKey.wasPressedThisFrame) owner.TogglePanel();
            if (Keyboard.current.rKey.wasPressedThisFrame) owner.Recenter();
        }
        private InputAction Action(string name, string binding, InputActionType type = InputActionType.Button)
        { var action = new InputAction(name, type, binding); action.Enable(); actions.Add(action); return action; }
        private InputAction Axis(string name, string negative, string positive)
        {
            var action = new InputAction(name, InputActionType.Value);
            action.AddCompositeBinding("1DAxis").With("Negative", negative).With("Positive", positive);
            action.Enable(); actions.Add(action); return action;
        }
        private static void Value<T>(XRInputValueReader<T> reader, InputAction action) where T : struct
        { reader.inputSourceMode = XRInputValueReader.InputSourceMode.InputAction; reader.inputAction = action; }
        private XRInputButtonReader Button(string name, string binding)
        {
            var reader = new XRInputButtonReader(name);
            reader.inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction;
            var action = Action(name, binding); reader.inputActionPerformed = action; reader.inputActionValue = action; return reader;
        }
        private void OnDestroy() { foreach (var action in actions) action.Dispose(); }
    }
}

#endif
