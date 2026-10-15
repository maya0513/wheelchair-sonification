using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using WheelchairSonification.Core;

namespace WheelchairSonification.Runtime
{
    internal sealed class DemoUi
    {
        private readonly Font font;
        public static readonly Color Ink = new Color32(31, 47, 62, 255);
        public static readonly Color Accent = new Color32(24, 105, 105, 255);
        public static readonly Color Muted = new Color32(94, 112, 129, 255);
        public DemoUi(Font font) { this.font = font; }

        public static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static RectTransform Canvas(string name, Transform parent, int order)
        {
            var canvas = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.transform.SetParent(parent, false);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 960);
            scaler.matchWidthOrHeight = 1;
            return (RectTransform)canvas.transform;
        }

        public static void EventSystem(Transform parent)
        {
            var events = new GameObject("DemoEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(parent, false);
        }

        public static Image Fill(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public Text Text(Transform parent, string name, string value, float x, float y, float width, float height,
            int size = 18, Color? color = null, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var rect = Rect(name, parent, x, y, width, height);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color ?? Ink;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        public RectTransform Card(Transform parent, string title, float x, float y, float width, float height)
        {
            var rect = Rect(title, parent, x, y, width, height);
            Fill(rect, Color.white).raycastTarget = false;
            Text(rect, "Title", title, 22, 12, width - 44, 36, 22);
            return rect;
        }

        public Button Button(Transform parent, string title, float x, float y, float width, UnityAction action,
            bool primary = false)
        {
            var rect = Rect(title, parent, x, y, width, 44);
            var image = Fill(rect, primary ? Accent : new Color32(230, 237, 243, 255));
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.disabledColor = new Color32(200, 207, 214, 180);
            button.colors = colors;
            Text(rect, "Label", title, 6, 0, width - 12, 44, 18,
                primary ? Color.white : Ink, TextAnchor.MiddleCenter);
            button.onClick.AddListener(action);
            return button;
        }

        public InputField Input(Transform parent, string name, string value, float x, float y, float width)
        {
            var rect = Rect(name, parent, x, y, width, 42);
            var image = Fill(rect, new Color32(241, 245, 248, 255));
            var field = rect.gameObject.AddComponent<InputField>();
            field.targetGraphic = image;
            field.textComponent = Text(rect, "Value", value, 12, 0, width - 24, 42, 19);
            field.text = value;
            field.characterLimit = 300;
            return field;
        }

        public Toggle Toggle(Transform parent, string title, bool value, float x, float y, float width)
        {
            var rect = Rect(title, parent, x, y, width, 34);
            var toggle = rect.gameObject.AddComponent<Toggle>();
            var box = Rect("Box", rect, 0, 5, 24, 24);
            toggle.targetGraphic = Fill(box, new Color32(225, 234, 240, 255));
            var mark = Rect("Check", box, 5, 5, 14, 14);
            toggle.graphic = Fill(mark, Accent);
            Text(rect, "Label", title, 36, 0, width - 36, 34);
            toggle.isOn = value;
            return toggle;
        }

        public Dropdown Dropdown(Transform parent, string name, string[] options, float x, float y, float width)
        {
            var rect = Rect(name, parent, x, y, width, 42);
            var image = Fill(rect, new Color32(241, 245, 248, 255));
            var dropdown = rect.gameObject.AddComponent<Dropdown>();
            dropdown.targetGraphic = image;
            dropdown.captionText = Text(rect, "Caption", "", 12, 0, width - 44, 42, 18);
            Text(rect, "Arrow", "▾", width - 30, 0, 24, 42, 18);

            var template = Rect("Template", rect, 0, 42, width, 210);
            Fill(template, Color.white);
            var scroll = template.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Rect("Viewport", template, 0, 0, width, 210);
            Fill(viewport, Color.white);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var content = Rect("Content", viewport, 0, 0, width, 40);
            var item = Rect("Item", content, 0, 0, width, 40);
            var itemToggle = item.gameObject.AddComponent<Toggle>();
            itemToggle.targetGraphic = Fill(item, new Color32(237, 244, 247, 255));
            var selection = Rect("Selection", item, 2, 2, 5, 36);
            itemToggle.graphic = Fill(selection, Accent);
            dropdown.itemText = Text(item, "Item Label", "", 14, 0, width - 20, 40, 18);
            scroll.viewport = viewport;
            scroll.content = content;
            dropdown.template = template;
            dropdown.AddOptions(new System.Collections.Generic.List<string>(options));
            template.gameObject.SetActive(false);
            return dropdown;
        }

        public Slider Volume(Transform parent, float x, float y, float width)
        {
            var rect = Rect("Digital volume", parent, x, y, width, 34);
            Fill(Rect("Track", rect, 0, 14, width, 6), new Color32(216, 226, 234, 255));
            var fillArea = Rect("Fill area", rect, 0, 14, width, 6);
            var fill = Rect("Fill", fillArea, 0, 0, width, 6);
            Fill(fill, Accent);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0.5f, 0.5f);
            fill.anchoredPosition = Vector2.zero;
            fill.sizeDelta = Vector2.zero;
            var handleArea = Rect("Handle area", rect, 0, 6, width, 22);
            var handle = Rect("Handle", handleArea, 0, 0, 14, 22);
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.anchoredPosition = Vector2.zero;
            handle.sizeDelta = new Vector2(14, 0);
            var handleImage = Fill(handle, Accent);
            var slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = 0.1f;
            return slider;
        }
    }

    public sealed class GaugeView
    {
        private readonly GameObject root;
        private readonly GameObject[] markers = new GameObject[3];
        public bool Visible => root.activeSelf;
        public FeedbackState? Marker { get; private set; }
        public RectTransform Root => (RectTransform)root.transform;

        internal GaugeView(DemoUi ui, Transform canvas)
        {
            var rect = DemoUi.Rect("Participant gauge", canvas, 0, 0, 360, 96);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = new Vector2(0, 24);
            root = rect.gameObject;
            DemoUi.Fill(rect, DemoUi.Ink).raycastTarget = false;
            string[] labels = { "短い", "目標範囲", "長い" };
            for (int i = 0; i < 3; i++)
            {
                var cell = DemoUi.Rect("Region " + i, rect, i * 120 + 1, 1, 118, 94);
                DemoUi.Fill(cell, Color.white).raycastTarget = false;
                ui.Text(cell, "Label", labels[i], 0, 9, 118, 34, 19,
                    alignment: TextAnchor.MiddleCenter);
                markers[i] = ui.Text(cell, "Marker", "●", 0, 48, 118, 34, 23,
                    alignment: TextAnchor.MiddleCenter).gameObject;
            }
            HideMarker();
            SetVisible(false);
        }

        public void ShowMarker(FeedbackState state)
        {
            Marker = state;
            for (int i = 0; i < markers.Length; i++) markers[i].SetActive(i == (int)state);
        }
        public void HideMarker()
        {
            Marker = null;
            foreach (var marker in markers) marker.SetActive(false);
        }
        public void SetVisible(bool visible)
        {
            if (!visible) HideMarker();
            root.SetActive(visible);
        }
    }
}
