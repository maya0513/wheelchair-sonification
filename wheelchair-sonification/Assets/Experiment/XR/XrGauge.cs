using System;
using UnityEngine;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.XR
{
    public sealed class XrGauge : MonoBehaviour, IFeedbackGauge
    {
        public RectTransform leftFill,
            rightFill;
        public float fullHeight = 320;
        public FeedbackDisplayAnchor Anchor { get; } = new FeedbackDisplayAnchor();
        bool requestedVisible,
            anchorValid;
        public bool Visible => gameObject.activeSelf;
        public double Left { get; private set; }
        public double Right { get; private set; }

        void Awake()
        {
            foreach (var label in GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                label.outlineColor = new Color32(0, 0, 0, 230);
                label.outlineWidth = .2f;
                label.fontMaterial.EnableKeyword("OUTLINE_ON");
                label.UpdateMeshPadding();
            }
        }

        public void SetVisible(bool visible)
        {
            requestedVisible = visible;
            gameObject.SetActive(visible && anchorValid);
        }

        public void SetValues(double left, double right)
        {
            Left = Mathf.Clamp01((float)left);
            Right = Mathf.Clamp01((float)right);
            leftFill.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                fullHeight * (float)Left
            );
            rightFill.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                fullHeight * (float)Right
            );
        }

        public void Follow(
            PoseFrame frame,
            TrackingCalibration calibration,
            Transform trackingOrigin
        )
        {
            anchorValid = Anchor.IsValid(frame, calibration);
            if (anchorValid)
            {
                var pose = Anchor.Pose(frame, calibration);
                transform.SetPositionAndRotation(
                    trackingOrigin.TransformPoint(pose.position),
                    trackingOrigin.rotation * pose.rotation
                );
            }
            gameObject.SetActive(requestedVisible && anchorValid);
        }

        public void Invalidate()
        {
            Anchor.Invalidate();
            anchorValid = false;
            SetVisible(false);
        }

        public static XrGauge InstantiateFor(Camera camera)
        {
            var prefab = Resources.Load<GameObject>("BilateralFeedback");
            if (prefab == null)
                throw new InvalidOperationException("左右速度UIのPrefabがありません。");
            var root = Instantiate(prefab, camera.transform.parent);
            var rect = root.GetComponent<RectTransform>();
            var gauge = root.GetComponent<XrGauge>();
            var placement = gauge.Anchor.Placement;
            rect.localScale =
                Vector3.one
                * (
                    2
                    * placement.distanceMeters
                    * Mathf.Tan(placement.barHeightDegrees / 2 * Mathf.Deg2Rad)
                    / gauge.fullHeight
                );
            root.GetComponent<Canvas>().worldCamera = camera;
            gauge.SetValues(0, 0);
            gauge.SetVisible(false);
            return gauge;
        }
    }
}
