using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WheelchairSonification.Core;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.XR
{
    // Logical canvas units at 1 mm/unit, 1.4 m from the user. No automatic text shrinking.
    public sealed class XrUi
    {
        public const float Width = 1080, Height = 840, Distance = 1.4f, Scale = .001f;
        public static readonly Color Ink = new Color32(20, 20, 20, 255);
        public static readonly Color Paper = new Color32(250, 250, 247, 255);
        public static readonly Color Border = new Color32(110, 110, 110, 255);
        public static readonly Color Blue = new Color32(0, 68, 160, 255);
        private readonly TMP_FontAsset font;

        public XrUi()
        {
            font = Resources.Load<TMP_FontAsset>("XrJapanese");
            if (font == null) throw new InvalidOperationException("XR日本語SDFフォントがありません。Experiment/Prepare XR resourcesを実行してください。");
        }

        public TMP_Text Text(Transform parent, string name, string text, float x, float y, float width,
            float height = 72, float size = 44, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var rect = DemoUi.Rect(name, parent, x, y, width, Mathf.Max(height, size * 1.5f));
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Truncate;
            label.alignment = alignment;
            label.color = Ink;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        public Button Button(Transform parent, string name, string text, float x, float y, float width,
            Action action, bool selected = false)
        {
            var rect = DemoUi.Rect(name, parent, x, y, width, 88);
            var fill = DemoUi.Fill(rect, Paper);
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(2, -2);
            outline.effectColor = selected ? Blue : Border;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color32(215, 228, 248, 255);
            colors.pressedColor = new Color32(180, 204, 238, 255);
            colors.disabledColor = new Color32(195, 195, 195, 255);
            button.colors = colors;
            var label = Text(rect, "Label", text, 12, 0, width - 24, 88, 44, TextAlignmentOptions.Center);
            if (selected) label.color = Blue;
            button.onClick.AddListener(() => action());
            return button;
        }

        public static void Selected(Button button, bool selected)
        {
            button.GetComponent<Outline>().effectColor = selected ? Blue : Border;
            button.GetComponentInChildren<TMP_Text>().color = selected ? Blue : Ink;
        }
    }

    public sealed class XrGauge : IFeedbackGauge
    {
        private readonly GameObject[] markers = new GameObject[3];
        public RectTransform Root { get; }
        public bool Visible => Root.gameObject.activeSelf;
        public FeedbackState? Marker { get; private set; }

        public XrGauge(XrUi ui, Transform parent)
        {
            Root = DemoUi.Rect("XR participant gauge", parent, 0, 0, 600, 200);
            Root.anchorMin = Root.anchorMax = Root.pivot = new Vector2(.5f, .5f);
            Root.anchoredPosition = Vector2.zero;
            DemoUi.Fill(Root, XrUi.Ink).raycastTarget = false;
            string[] labels = { "短い", "目標範囲", "長い" };
            for (int i = 0; i < 3; i++)
            {
                var cell = DemoUi.Rect("Region " + i, Root, i * 200 + 3, 3, 194, 194);
                DemoUi.Fill(cell, XrUi.Paper).raycastTarget = false;
                ui.Text(cell, "Label", labels[i], 4, 20, 186, 64, 40, TextAlignmentOptions.Center);
                // A filled rectangle avoids dependence on the glyph shape of a bullet.
                var marker = DemoUi.Rect("Marker", cell, 72, 115, 50, 50);
                DemoUi.Fill(marker, XrUi.Ink).raycastTarget = false;
                markers[i] = marker.gameObject;
            }
            SetVisible(false);
        }

        public void ShowMarker(FeedbackState state)
        {
            Marker = state;
            for (int i = 0; i < 3; i++) markers[i].SetActive(i == (int)state);
        }
        public void HideMarker() { Marker = null; foreach (var marker in markers) marker.SetActive(false); }
        public void SetVisible(bool visible) { if (!visible) HideMarker(); Root.gameObject.SetActive(visible); }
    }
}
