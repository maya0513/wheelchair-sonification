using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WheelchairSonification.Runtime;
namespace WheelchairSonification.XR
{
    public static class XrUi
    {
        public static readonly Color Paper = new Color32(250, 251, 253, 255), Ink = new Color32(21, 26, 33, 255), Blue = new Color32(21, 92, 179, 255);
        public static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0,1); r.pivot = new Vector2(0,1); r.anchoredPosition = new Vector2(x,-y); r.sizeDelta = new Vector2(width,height); return r;
        }
        public static Image Fill(RectTransform r, Color color) { var image = r.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image; }
        public static TMP_Text Text(Transform parent, string name, string text, float x, float y, float width, float height, float size = 44)
        {
            var t = Rect(name,parent,x,y,width,height).gameObject.AddComponent<TextMeshProUGUI>(); t.font = Resources.Load<TMP_FontAsset>("XrJapanese");
            if (t.font == null) throw new InvalidOperationException("日本語SDFフォントがありません。");
            t.text = text; t.fontSize = size; t.enableAutoSizing = false; t.color = Ink; t.alignment = TextAlignmentOptions.MidlineLeft;
            t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Truncate; t.raycastTarget = false; return t;
        }
        public static RectTransform Canvas(string name, Transform parent, Vector2 size)
        {
            var r = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<RectTransform>(); r.SetParent(parent,false);
            r.sizeDelta = size; r.pivot = new Vector2(.5f,.5f); var c = r.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace; return r;
        }
    }
}
