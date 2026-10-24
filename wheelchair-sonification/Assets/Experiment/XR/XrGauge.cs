using System;
using UnityEngine;
using WheelchairSonification.Runtime;
namespace WheelchairSonification.XR
{
    // Editable, serialized references in Resources/BilateralFeedback.prefab. The APK and PNG capture instantiate this asset.
    public sealed class XrGauge : MonoBehaviour, IFeedbackGauge
    {
        public RectTransform leftFill, rightFill;
        public float fullWidth = 304;
        public bool Visible => gameObject.activeSelf;
        public void SetVisible(bool visible) => gameObject.SetActive(visible);
        public void SetValues(double left, double right)
        {
            leftFill.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, fullWidth * Mathf.Clamp01((float)left));
            rightFill.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, fullWidth * Mathf.Clamp01((float)right));
        }
        public static XrGauge InstantiateFor(Camera camera)
        {
            var prefab = Resources.Load<GameObject>("BilateralFeedback");
            if (prefab == null) throw new InvalidOperationException("左右速度UIのPrefabがありません。");
            var root = Instantiate(prefab, camera.transform); var rect = root.GetComponent<RectTransform>();
            const float distance = 1.4f;
            rect.localPosition = new Vector3(0,-Mathf.Tan(10 * Mathf.Deg2Rad) * distance,distance); rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * (2 * distance * Mathf.Tan(12 * Mathf.Deg2Rad) / 720);
            root.GetComponent<Canvas>().worldCamera = camera; var gauge = root.GetComponent<XrGauge>(); gauge.SetValues(0,0); gauge.SetVisible(false); return gauge;
        }
    }
}
